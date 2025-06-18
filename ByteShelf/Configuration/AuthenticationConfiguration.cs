namespace ByteShelf.Configuration
{
    public class AuthenticationConfiguration
    {
        public string ApiKey { get; set; } = string.Empty;
        
        public bool RequireAuthentication { get; set; } = true;
    }
}