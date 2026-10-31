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
} as const;
