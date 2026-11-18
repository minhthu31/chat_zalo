# Pending Admin transfers — FE integration

GET /api/conversations/{id}/admin-transfer (alias: /api/groups/{id}/admin-transfer).

Requires the existing Identity cookie. GET does not require CSRF. Returns 200 AdminTransferDto[] ordered by createdAt, then id. Only Pending requests where the caller is fromUserId or toUserId are returned. An unrelated active member receives []; outsiders, former members and dissolved groups receive 403 GROUP_ACCESS_DENIED. Uses the existing GetGroup permission/error behavior for invalid conversation IDs/kinds.

FE: load this endpoint on group details entry and refresh periodically while the page is open. Display Accept/Reject for items with toUserId == currentUser.id. Refresh after proposing or resolving. Use existing POST accept/reject endpoints with CSRF; keep handling 409 ADMIN_TRANSFER_ALREADY_RESOLVED. A manually shared link is no longer needed. No schema migration or existing DTO/endpoint change.
