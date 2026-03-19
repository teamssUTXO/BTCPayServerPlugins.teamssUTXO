# Uptime Checker — Plugin v1

<img width="2876" height="1461" alt="Image" src="https://github.com/user-attachments/assets/38127d3d-7a85-483e-960a-ba5a207027c3" />

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

## Checks Logs

❗This feature is disabled by default.

Save your check history with a configurable retention period ranging from 1 day to 1 year (365 days) — letting you review past incidents, downtime events, and more.

You can also filter results by URL, status, date and UP/DOWN transitions. <br>

<img width="2303" height="1433" alt="Image" src="https://github.com/user-attachments/assets/d26fa9bb-1d9e-45a7-9b35-702d632c0f2f" />


