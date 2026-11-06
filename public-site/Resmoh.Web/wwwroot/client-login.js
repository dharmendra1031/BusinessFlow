'use strict';
const email = document.getElementById('client-email');
const code = document.getElementById('client-code');
const message = document.getElementById('login-message');
const requestButton = document.getElementById('request-code');
const verifyButton = document.getElementById('verify-code');
const emailStage = document.getElementById('email-stage');
const codeStage = document.getElementById('code-stage');

requestButton.addEventListener('click', async () => {
  if (!email.reportValidity()) return;
  requestButton.disabled = true;
  message.textContent = '';
  try {
    const response = await fetch('/api/client/auth/request-code', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.value.trim() })
    });
    if (!response.ok) throw new Error('request failed');
    emailStage.hidden = true;
    codeStage.hidden = false;
    message.textContent = 'If this email has approved client access, we have sent a verification code.';
    code.focus();
  } catch {
    message.textContent = 'Unable to request a code. Please try again later.';
  } finally {
    requestButton.disabled = false;
  }
});
document.getElementById('change-email').addEventListener('click', () => {
  code.value = '';
  codeStage.hidden = true;
  emailStage.hidden = false;
  message.textContent = '';
  email.focus();
});
document.getElementById('client-login').addEventListener('submit', async (event) => {
  event.preventDefault();
  if (!codeStage.hidden && !code.reportValidity()) return;
  verifyButton.disabled = true;
  message.textContent = '';
  try {
    const response = await fetch('/api/client/auth/verify-code', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.value.trim(), code: code.value.trim() })
    });
    if (!response.ok) {
      message.textContent = response.status === 429
        ? 'Too many attempts. Please try again later.'
        : 'Invalid or expired code. Check the code or request a new one.';
      return;
    }
    window.location.assign('/client/account/');
  } catch {
    message.textContent = 'Sign in is temporarily unavailable. Please try again.';
  } finally {
    verifyButton.disabled = false;
  }
});
