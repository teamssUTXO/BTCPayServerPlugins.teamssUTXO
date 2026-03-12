# Uptime Checker — Plugin v1

<img width="1920" height="912" alt="Image" src="https://github.com/user-attachments/assets/f5c4c4ad-afc7-4cc7-837e-77cf0720f9b2" />

## Objective

This plugin check and monitor the availability of web services by performing HTTP checks on a list of URLs configured by the store administrator.

## How it works

The administrator creates checks, each associated with :

- A URL to check (http:// or https://)
- A verification interval
- A list of email adresses to notify

A background worker executes the checks according to their configured interval. An HTTP response between 200 and 399 is considered up. Any other code or network failure (timeout, DNS, TLS, etc.) is considered down.

## Email Alerts

The plugin sends notifications only during state transitions:

- Down: An alert email is sent (only once).

- Up (recovery): A recovery email is sent.

As long as the service remains in the same state, no additional emails are sent.

<img width="1920" height="912" alt="Image" src="https://github.com/user-attachments/assets/b6bda301-f051-4766-a3c3-6467726630d6" />

## Checks Logs

❗This feature is disabled by default.

You can save your check history with a configurable retention period ranging from 1 day to 1 year (365 days).

This allows you to review past incidents, downtime events, and more.

<img width="2878" height="1463" alt="Image" src="https://github.com/user-attachments/assets/7249754a-31db-42c7-ae45-6d0ede43ddec" />

Filtering by check, days, and status will come soon.
