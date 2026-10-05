---
id: arch:service-catalog
title: Architecture — service catalog and dependencies
kind: architecture
tags: [architecture, ownership, dependencies]
---
# Architecture — service catalog and dependencies

## checkout-api
.NET 10 ASP.NET Core API, 3 replicas on AKS. Depends on orders-db (Azure SQL, max pool size 100 per
instance) and payments-api. Owner: team-checkout.

## search-api
.NET 10 API, 4 replicas (HPA max 16). CPU-bound ranking in RankingService. Depends on search-db.
Owner: team-search.

## payments-api
.NET 10 API, 3 replicas. Calls the external payment-gateway (p50 ~800 ms, p99 ~2 s); the client timeout
must stay above 3 s. Configuration is synchronized by config-sync-bot. Owner: team-payments.
