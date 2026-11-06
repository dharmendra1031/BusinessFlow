'use strict';
const company = document.getElementById('account-company');
const person = document.getElementById('account-person');
const email = document.getElementById('account-email');
const message = document.getElementById('account-message');
const logout = document.getElementById('client-logout');

async function loadAccount() {
  try {
    const response = await fetch('/api/client/me', { credentials: 'same-origin', cache: 'no-store' });
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
  } catch {
    company.textContent = 'Account temporarily unavailable';
    message.textContent = 'Please refresh or try again later.';
  }
}
logout.addEventListener('click', async () => {
  logout.disabled = true;
  try {
    const response = await fetch('/api/client/auth/logout', { method: 'POST', credentials: 'same-origin' });
    if (!response.ok) throw new Error('logout failed');
    window.location.assign('/client/login/');
  } catch {
    message.textContent = 'Could not sign out. Please try again.';
    logout.disabled = false;
  }
});
void loadAccount();
