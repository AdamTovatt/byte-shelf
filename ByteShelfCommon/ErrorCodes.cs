namespace ByteShelfCommon
{
    /// <summary>The values of <see cref="ErrorResponse.Code"/>.</summary>
    public static class ErrorCodes
    {
        /// <summary>The file, directory, shelf or upload does not exist, or the caller cannot see it.</summary>
        public const string NotFound = "not_found";

        /// <summary>Something already exists at the path, or the request conflicts with the current state.</summary>
        public const string Conflict = "conflict";

        /// <summary>The shelf's quota cannot hold the bytes.</summary>
        public const string QuotaExceeded = "quota_exceeded";

        /// <summary>The request is malformed: a bad path, a negative size, a chunk at the wrong offset.</summary>
        public const string InvalidRequest = "invalid_request";

        /// <summary>A password or other credential the request carried is wrong.</summary>
        public const string InvalidCredentials = "invalid_credentials";

        /// <summary>The request body is larger than the endpoint accepts.</summary>
        public const string TooLarge = "too_large";

        /// <summary>
        /// The request carries no valid credentials: no token or key, an expired token, or a key that
        /// does not check out. Answered with status 401, often without a body.
        /// </summary>
        public const string Unauthenticated = "unauthenticated";

        /// <summary>The caller can see the shelf but its permission does not allow the operation.</summary>
        public const string PermissionDenied = "permission_denied";

        /// <summary>The server is a replica and refuses writes.</summary>
        public const string ReadOnlyReplica = "read_only_replica";

        /// <summary>A replica's primary could not be reached, or what it answered could not be used.</summary>
        public const string PrimaryFailed = "primary_failed";
    }
}
