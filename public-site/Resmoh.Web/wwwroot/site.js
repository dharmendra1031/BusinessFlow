'use strict';

const year = document.getElementById('year');
if (year) year.textContent = String(new Date().getFullYear());

const form = document.getElementById('project-form');
if (form) {
  const fields = [...form.querySelectorAll('[data-step]')];
  const stepLabel = document.getElementById('step-label');
  const progress = document.getElementById('form-progress');
  const next = document.getElementById('next');
  const back = document.getElementById('back');
  const submit = document.getElementById('submit');
  const notice = document.getElementById('form-message');
  const success = document.getElementById('form-success');
  const service = document.getElementById('service');
  const details = document.getElementById('details');
  const serviceHint = document.getElementById('service-hint');
  const summary = document.getElementById('project-summary');

  const prompts = {
    Website: 'What should visitors be able to do? Mention pages, forms or bookings.',
    'Mobile App': 'Who will use the app? Tell us about the main screens and workflows.',
    'ERP / CRM': 'Which departments and daily operations should this system manage?',
    POS: 'Tell us about stores, billing, inventory, branches and payment workflows.',
    'AI Automation': 'Which repetitive process do you want to automate?',
    'Custom Software': 'What is your business problem and what would a good solution do?'
  };

  let step = 1;
  let key = '';
  let lastAttempt = '';
  let sending = false;

  const read = (name) => {
    const input = form.elements.namedItem(name);
    return input ? String(input.value || '').trim() : '';
  };

  function showStep(index) {
    step = index;
    fields.forEach(field => { field.hidden = Number(field.dataset.step) !== step; });
    stepLabel.textContent = 'Step ' + step + ' of 3';
    progress.style.width = String(step * 100 / 3) + '%';
    back.hidden = step === 1;
    next.hidden = step === 3;
    submit.hidden = step !== 3;
    notice.hidden = true;

    if (step === 3) {
      summary.replaceChildren();
      const title = document.createElement('h3');
      title.textContent = 'Your project summary';
      summary.append(title);
      for (const [name, value] of [
        ['Service', read('service')],
        ['Project', read('details')],
        ['Timeline', read('timeline')],
        ['Budget', read('budget') || 'Not sure']
      ]) {
        const line = document.createElement('p');
        line.textContent = name + ': ' + value;
        summary.append(line);
      }
    }
    const input = fields[step - 1].querySelector('select, textarea, input:not([type=checkbox])');
    input?.focus();
  }

  function validate(index) {
    const container = fields[index - 1];
    const inputs = [...container.querySelectorAll('input, textarea, select')];

    for (const input of inputs) {
      input.setCustomValidity('');
      if (input.id === 'phone') {
        const phone = input.value.trim();
        const digits = (phone.match(/\d/g) || []).length;
        if (!/^\+?[0-9() .-]{7,30}$/.test(phone) || digits < 7 || digits > 15)
          input.setCustomValidity('Enter a phone number with 7 to 15 digits.');
      }
      if (!input.checkValidity()) {
        input.reportValidity();
        input.focus();
        return false;
      }
    }
    return true;
  }

  function showMessage(text) {
    notice.textContent = text;
    notice.hidden = false;
    notice.classList.remove('info');
    notice.focus?.();
  }

  function newKey() {
    const values = new Uint32Array(3);
    crypto.getRandomValues(values);
    return Array.from(values, value => String(value % 100000000).padStart(8, '0')).join('');
  }

  function updatePrompt() {
    serviceHint.textContent = prompts[service.value] ||
      'Choose the closest option. We can refine it together.';
  }

  const requestedService = new URLSearchParams(window.location.search).get('service');
  if (Object.hasOwn(prompts, requestedService)) service.value = requestedService;
  updatePrompt();
  service.addEventListener('change', updatePrompt);
  details.addEventListener('input', () => {
    document.getElementById('details-count').textContent =
      details.value.length + ' / 1600';
  });

  next.addEventListener('click', () => {
    if (validate(step)) showStep(step + 1);
  });
  back.addEventListener('click', () => showStep(step - 1));

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    if (sending) return;
    for (let i = 1; i <= 3; i++) {
      if (!validate(i)) {
        showStep(i);
        validate(i);
        return;
      }
    }

    const values = {
      service: read('service'),
      name: read('name'),
      phone: read('phone'),
      email: read('email') || null,
      company: read('company') || null,
      country: read('country') || null,
      budget: read('budget') || null,
      timeline: read('timeline'),
      details: read('details'),
      website: '',
      consentAccepted: form.elements.namedItem('consent').checked
    };
    const snapshot = JSON.stringify(values);
    if (!key || (lastAttempt && snapshot !== lastAttempt)) key = newKey();
    lastAttempt = snapshot;

    sending = true;
    submit.disabled = true;
    submit.textContent = 'Submitting…';
    notice.hidden = true;

    try {
      const response = await fetch('/api/project-requests', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ submissionKey: key, ...values })
      });
      if (!response.ok) {
        showMessage(response.status === 429
          ? 'Too many attempts. Please try again in a few minutes.'
          : response.status === 400
            ? 'Please review the information and try again.'
            : 'We could not confirm your request. Please retry without changing details; this will not create a second request if the first was received.');
        return;
      }
      form.hidden = true;
      success.hidden = false;
      success.focus();
      key = '';
    } catch {
      showMessage('Connection failed. Check your internet connection, then retry. Your form details are preserved.');
    } finally {
      sending = false;
      submit.disabled = false;
      submit.textContent = 'Submit project request →';
    }
  });

  showStep(1);
}
