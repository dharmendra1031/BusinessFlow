import type { FormEvent, KeyboardEvent } from 'react';

type Field = HTMLInputElement | HTMLTextAreaElement;

export function clearFieldError(event: FormEvent<HTMLFormElement>) {
  const field = event.target;
  if (field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement)
    field.setCustomValidity('');
}

export function validateForm(form: HTMLFormElement): boolean {
  const fields = form.querySelectorAll<Field>('input[required], textarea[required]');
  for (const field of fields) {
    const value = field instanceof HTMLInputElement && field.type === 'password'
      ? field.value : field.value.trim();
    const minimum = field instanceof HTMLInputElement ? field.minLength : -1;
    if (value && (minimum < 0 || value.length >= minimum)) continue;
    field.setCustomValidity(value ? 'Enter at least ' + minimum + ' characters.' : 'This field cannot be blank.');
    field.reportValidity();
    field.focus();
    return false;
  }
  for (const field of form.querySelectorAll<HTMLInputElement>('input[type="tel"]')) {
    const value = field.value.trim();
    if (!value) continue;
    const digits = value.replace(/\D/g, '').length;
    if (/^\+?[0-9(). -]+$/.test(value) && digits >= 7 && digits <= 15) continue;
    field.setCustomValidity('Enter a valid phone number with 7 to 15 digits.');
    field.reportValidity();
    field.focus();
    return false;
  }
  for (const field of form.querySelectorAll<HTMLInputElement>('input[type="number"]')) {
    const value = field.value.trim();
    if (!value || /^\d{1,15}(\.\d{1,3})?$/.test(value)) continue;
    field.setCustomValidity('Enter an amount from 0 to 999,999,999,999,999.999 with up to 3 decimals.');
    field.reportValidity();
    field.focus();
    return false;
  }
  return form.reportValidity();
}

export function handleFormKeyDown(event: KeyboardEvent<HTMLFormElement>) {
  if (event.key !== 'Enter' || event.shiftKey || event.ctrlKey || event.altKey ||
      event.metaKey || event.nativeEvent.isComposing) return;

  const field = event.target;
  if (!(field instanceof HTMLInputElement) ||
      ['button', 'submit', 'checkbox', 'radio', 'file'].includes(field.type)) return;

  const controls = Array.from(event.currentTarget.querySelectorAll<
    HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement
  >('input, select, textarea, button')).filter(control =>
    !control.disabled && control.tabIndex !== -1 && control.getClientRects().length > 0 &&
    (!(control instanceof HTMLButtonElement) || control.type === 'submit'));

  const next = controls[controls.indexOf(field) + 1];
  if (next) {
    event.preventDefault();
    if ((next instanceof HTMLButtonElement || next instanceof HTMLInputElement) &&
        next.type === 'submit') {
      event.currentTarget.requestSubmit(next);
    } else {
      next.focus();
    }
  }
}

export function handleDialogKeyDown(event: KeyboardEvent<HTMLElement>, close: () => void) {
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    close();
    return;
  }
  if (event.key !== 'Tab') return;

  const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(
    'a[href], button, input, select, textarea, [tabindex]:not([tabindex="-1"])'
  )).filter(control => !('disabled' in control && control.disabled) &&
    control.getClientRects().length > 0);

  if (!controls.length) return;
  const first = controls[0];
  const last = controls[controls.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}
