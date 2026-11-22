# ADR-0007 — Google Cloud Storage claim-check provider is deferred

**Status:** Accepted · **Date:** 2026-11-19

## Context

`CQRS-62` ships file-system, Azure Blob, and Amazon S3 claim-check providers behind the
provider-neutral `IClaimCheckStore` seam. Google Cloud Storage (GCS) is the remaining major
object store in the peer landscape, but no committed deployment in this repository targets GCS.
Adding it now would introduce `Google.Cloud.Storage.V1`, a second cloud authentication model, and
an emulator (`fake-gcs-server`) to the contract suite without a consumer to characterize it
against.

## Decision

Do **not** ship `Jeninnet.CQRS.ClaimCheck.GoogleCloudStorage` in the current wave. GCS users can
implement `IClaimCheckStore` (and `IClaimCheckStoreMaintenance`) directly; the core package and
the durable rows depend only on the two interfaces, not on a provider type.

Built-in encryption is likewise out of scope: the shipped providers store bytes as-is and rely on
volume encryption, Azure Storage service-side/CMK encryption, or S3 SSE-S3/SSE-KMS. The provider
READMEs document that boundary.

## Consequences

- The package surface stays at three tested providers; no new cloud SDK or emulator is pulled into
  the repository.
- A GCS deployment must provide its own adapter until the revisit trigger is met.
- Reference-aware TTL cleanup is provider-specific; a GCS adapter must implement
  `IClaimCheckStoreMaintenance` to participate.

## Revisit trigger

Add a GCS provider when a committed deployment uses GCS, the runtime identity/permissions model is
documented, and the shared contract can run against `fake-gcs-server` (or an equivalent emulator)
in CI. Target review date: **2027-03-31**.
