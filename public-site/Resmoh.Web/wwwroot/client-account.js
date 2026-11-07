'use strict';

const company = document.getElementById('account-company');
const person = document.getElementById('account-person');
const email = document.getElementById('account-email');
const message = document.getElementById('account-message');
const logout = document.getElementById('client-logout');
const documents = document.getElementById('client-documents');
const detail = document.getElementById('document-detail');

function money(amount, currency) {
  try {
    return new Intl.NumberFormat('en-IN', {
      style: 'currency', currency
    }).format(amount);
  } catch {
    return currency + ' ' + amount;
  }
}

function formatDate(date) {
  return date ? new Date(date).toLocaleDateString('en-IN', { dateStyle: 'medium' }) : '—';
}

function addText(parent, name, value) {
  const element = document.createElement('p');
  element.textContent = name + ': ' + (value ?? '—');
  parent.appendChild(element);
}

async function showDetail(kind, id) {
  const title = document.getElementById('document-title');
  const body = document.getElementById('document-body');
  body.replaceChildren();
  detail.hidden = false;
  title.textContent = 'Loading document…';

  try {
    const response = await fetch('/api/client/' + kind + '/' + id, {
      credentials: 'same-origin', cache: 'no-store'
    });
    if (response.status === 401) {
      window.location.replace('/client/login/');
      return;
    }
    if (!response.ok) throw new Error('unavailable');
    const data = await response.json();
    title.textContent = (kind === 'quotations' ? 'Quotation #' : 'Invoice #') + data.id;
    addText(body, 'Status', data.status);
    addText(body, 'Description', data.title ?? data.description);
    addText(body, 'Amount', money(data.amount, data.currency));
    if (kind === 'quotations') {
      addText(body, 'Scope', data.scope);
      addText(body, 'Payment terms', data.paymentTerms);
      addText(body, 'Valid until', formatDate(data.validUntil));
      for (const item of data.items ?? []) {
        addText(body, item.description, item.quantity + ' × ' +
          money(item.unitPrice, data.currency) + ' = ' + money(item.totalAmount, data.currency));
      }
    } else {
      addText(body, 'Due date', formatDate(data.dueDate));
      addText(body, 'Paid', money(data.paidAmount, data.currency));
      addText(body, 'Credits', money(data.creditedAmount, data.currency));
      addText(body, 'Outstanding', money(data.outstanding, data.currency));
    }
    title.focus();
  } catch {
    title.textContent = 'Document unavailable';
    addText(body, 'Message', 'Please try again later.');
  }
}

function renderRows(kind, data) {
  const container = document.getElementById(kind + '-list');
  container.replaceChildren();
  const tableWrap = document.createElement('div');
  tableWrap.className = 'client-table-wrap';
  const table = document.createElement('table');
  table.className = 'client-table';
  const head = document.createElement('tr');
  for (const name of ['Reference', 'Description', 'Amount', 'Status', '']) {
    const th = document.createElement('th');
    th.textContent = name;
    head.appendChild(th);
  }
  table.createTHead().appendChild(head);

  const body = table.createTBody();
  for (const item of data.items) {
    const row = body.insertRow();
    for (const value of [
      (kind === 'quotations' ? 'Q-' : 'INV-') + item.id,
      item.title ?? item.description, money(item.amount, item.currency), item.status
    ]) {
      row.insertCell().textContent = value;
    }
    const cell = row.insertCell();
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = 'View';
    button.className = 'client-view';
    button.setAttribute('aria-label', 'View ' + kind.slice(0, -1) + ' ' + item.id);
    button.addEventListener('click', () => void showDetail(kind, item.id));
    cell.appendChild(button);
  }
  tableWrap.appendChild(table);
  container.appendChild(tableWrap);

  if (data.total > data.pageSize) {
    const pager = document.createElement('div');
    pager.className = 'client-pager';
    for (const [label, page] of [['Previous', data.page - 1], ['Next', data.page + 1]]) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = label;
      button.disabled = page < 1 || (page - 1) * data.pageSize >= data.total;
      button.addEventListener('click', () => void loadDocuments(kind, page));
      pager.appendChild(button);
    }
    container.appendChild(pager);
  }
}

async function loadDocuments(kind, page = 1) {
  const status = document.getElementById(kind + '-status');
  status.textContent = 'Loading…';
  try {
    const response = await fetch('/api/client/' + kind + '?page=' + page, {
      credentials: 'same-origin', cache: 'no-store'
    });
    if (response.status === 401) {
      window.location.replace('/client/login/');
      return;
    }
    if (!response.ok) throw new Error('unavailable');
    const data = await response.json();
    if (!data.items.length) {
      document.getElementById(kind + '-list').replaceChildren();
      status.textContent = page > 1 ? 'No more records.' : 'No shared ' + kind + ' yet.';
      return;
    }
    status.textContent = 'Showing page ' + data.page + ' of ' +
      Math.ceil(data.total / data.pageSize);
    renderRows(kind, data);
  } catch {
    status.textContent = 'Unable to load ' + kind + '. Please refresh the page.';
  }
}

async function loadAccount() {
  try {
    const response = await fetch('/api/client/me', {
      credentials: 'same-origin', cache: 'no-store'
    });
    if (response.status === 401) {
      window.location.replace('/client/login/');
      return;
    }
    if (!response.ok) throw new Error('unavailable');
    const data = await response.json();
    company.textContent = data.customerName;
    person.textContent = 'Contact: ' + data.contactName;
    email.textContent = 'Email: ' + data.email;
    message.textContent = 'Your client identity is verified.';
    logout.hidden = false;
    documents.hidden = false;
    await Promise.all([loadDocuments('quotations'), loadDocuments('invoices')]);
  } catch {
    company.textContent = 'Account temporarily unavailable';
    message.textContent = 'Please refresh or try again later.';
  }
}

document.getElementById('document-close').addEventListener('click', () => {
  detail.hidden = true;
  document.getElementById('client-documents').scrollIntoView({ block: 'start' });
});
logout.addEventListener('click', async () => {
  logout.disabled = true;
  try {
    const response = await fetch('/api/client/auth/logout', {
      method: 'POST', credentials: 'same-origin'
    });
    if (!response.ok) throw new Error('logout failed');
    window.location.assign('/client/login/');
  } catch {
    message.textContent = 'Could not sign out. Please try again.';
    logout.disabled = false;
  }
});
void loadAccount();
