/**
 * The machine-readable codes in the server's error bodies (`ByteShelfCommon/ErrorCodes.cs`), which the
 * OpenAPI document does not carry. A test checks this mirrors the server's list.
 */
export const ERROR_CODES = {
  notFound: 'not_found',
  conflict: 'conflict',
  quotaExceeded: 'quota_exceeded',
  invalidRequest: 'invalid_request',
  invalidCredentials: 'invalid_credentials',
  tooLarge: 'too_large',
  unauthenticated: 'unauthenticated',
  permissionDenied: 'permission_denied',
  readOnlyReplica: 'read_only_replica',
  primaryFailed: 'primary_failed',
  notReady: 'not_ready',
  rangeNotSatisfiable: 'range_not_satisfiable',
  // Never in a server's error body: ByteShelfClient's ranged read refuses changed contents with it.
  contentChanged: 'content_changed',
} as const;
