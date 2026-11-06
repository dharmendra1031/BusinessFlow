'use strict';

const statusHeading = document.getElementById('request-status');
const statusDate = document.getElementById('request-date');
const message = document.getElementById('request-message');
const refresh = document.getElementById('refresh-status');

async function loadStatus() {
  refresh.disabled = true;
  message.textContent = 'Checking the status with our team…';
  statusDate.hidden = true;
  try {
    const response = await fetch('/api/project-request/status', {
      credentials: 'same-origin',
      cache: 'no-store'
    });
    if (response.status === 401) {
      statusHeading.textContent = 'No active request found';
      message.textContent = 'Use the same browser where you submitted the request. A signed receipt is valid for 14 days.';
      return;
    }
    if (response.status === 404) {
      statusHeading.textContent = 'Request not found';
      message.textContent = 'We could not match the saved receipt to a submitted enquiry.';
      return;
    }
    if (!response.ok) throw new Error('status unavailable');
    const data = await response.json();
    statusHeading.textContent = data.status || 'Received';
    if (data.submittedAtUtc) {
      const date = new Date(data.submittedAtUtc);
      if (!Number.isNaN(date.getTime())) {
        statusDate.textContent = 'Submitted: ' + date.toLocaleString();
        statusDate.hidden = false;
      }
    }
    message.textContent = 'This status comes from your project request in BusinessFlow. For commercial details, our team will contact you directly.';
  } catch {
    statusHeading.textContent = 'Status temporarily unavailable';
    message.textContent = 'Please try again. Your submitted enquiry has not been changed.';
  } finally {
    refresh.disabled = false;
  }
}

refresh.addEventListener('click', loadStatus);
void loadStatus();
