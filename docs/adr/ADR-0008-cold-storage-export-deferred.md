# ADR-0008 — Cold-storage export is deferred

**Status:** Accepted · **Date:** 2026-11-19

## Context

`CQRS-64` adds policy-driven retention and archive tables for the durable stores. Operators may
eventually want to export archived rows to CSV, Parquet, or an external warehouse, but the
requirement is not part of the current parity matrix and no deployment in this repository has an
analytics target.

## Decision

Archive tables are the terminal retention surface for this wave. No CSV/Parquet exporter, object
storage sink, or warehouse connector ships in the packages. Consumers can query the archive tables
with EF Core or the database's native export tools.

## Consequences

- The sweeper has one clear responsibility: move or delete eligible rows under a lease.
- Archive schemas remain stable and queryable; no serialization format becomes a compatibility
  promise.
- A future exporter can be a separate package over the same tables without changing the stores.

## Revisit trigger

Add an exporter when a committed analytics/warehouse consumer exists, its schema and retention
requirements are documented, and the target format (CSV, Parquet, or a managed sink) is chosen.
Target review date: **2027-03-31**.
