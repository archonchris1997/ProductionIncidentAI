---
id: postmortem:INC-087
title: Postmortem INC-087 — checkout-api connection leak (v1.37)
kind: postmortem
tags: [checkout-api, orders-db, connection-leak, rollback, postmortem]
---
# Postmortem INC-087 — checkout-api connection leak (v1.37)

## Summary
Release v1.37 of checkout-api introduced a code path in OrderRepository that did not dispose SqlConnection.
The orders-db pool was exhausted within ~15 minutes, causing SQL timeouts and HTTP 500 on checkout.

## Resolution
Rollback to v1.36, then fix with `await using` and an integration test asserting pool usage stays bounded.

## Lessons
- A healthy *current* database snapshot can hide a historical pool saturation (pods restarted by probes).
- Add analyzer rule CA2000 (dispose objects before losing scope) as an error in Checkout.Infrastructure.
