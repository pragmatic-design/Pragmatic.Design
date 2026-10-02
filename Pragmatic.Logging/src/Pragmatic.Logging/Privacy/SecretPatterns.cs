namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Predefined patterns for detecting secrets and sensitive tokens in log data.
/// </summary>
public static class SecretPatterns
{
    /// <summary>
    /// API Keys and Authentication Tokens
    /// </summary>
    public static class ApiKeys
    {
        /// <summary>
        /// Generic API key pattern.
        /// </summary>
        /// <remarks>
        /// Trade-off: a bare 32-64 char alphanumeric run is indistinguishable from UUIDs
        /// (without dashes), hex hashes (MD5/SHA), base64 blobs, and content digests, so the
        /// old <c>\b[A-Za-z0-9]{32,64}\b</c> over-masked aggressively. We now require a
        /// key-like label/prefix near the value (<c>api_key</c>, <c>apikey</c>, <c>api-key</c>,
        /// <c>secret</c>, <c>token</c>, <c>access_key</c>, …) so the match is anchored to an
        /// actual credential assignment. The secret value is captured in group 1.
        /// This narrows recall (an unlabelled raw key won't match here) in exchange for a
        /// dramatic drop in false positives; provider-specific patterns (AWS, Google, Stripe,
        /// GitHub, …) still cover unlabelled keys with recognizable shapes.
        /// </remarks>
        public static readonly string Generic = @"(?i)\b(?:api[_-]?key|access[_-]?key|secret[_-]?key|client[_-]?secret|app[_-]?secret|api[_-]?secret|secret|token|apikey)\b\s*[:=]\s*['""]?([A-Za-z0-9][A-Za-z0-9_\-]{30,63})['""]?";

        /// <summary>AWS Access Key ID pattern</summary>
        public static readonly string AwsAccessKey = @"\b(AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16})\b";

        /// <summary>AWS Secret Access Key pattern</summary>
        public static readonly string AwsSecretKey = @"\b[A-Za-z0-9/+=]{40}\b";

        /// <summary>Google API Key pattern</summary>
        public static readonly string GoogleApiKey = @"\bAIza[0-9A-Za-z_-]{35}\b";

        /// <summary>GitHub Personal Access Token</summary>
        public static readonly string GitHubToken = @"\bgh[pousr]_[A-Za-z0-9_]{36,255}\b";

        /// <summary>Azure Client Secret</summary>
        public static readonly string AzureClientSecret = @"\b[A-Za-z0-9~._-]{34,40}\b";

        /// <summary>Stripe API Key</summary>
        public static readonly string StripeApiKey = @"\b(sk|pk)_(test|live)_[0-9a-zA-Z]{24,34}\b";

        /// <summary>SendGrid API Key</summary>
        public static readonly string SendGridApiKey = @"\bSG\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}\b";

        /// <summary>Slack Token</summary>
        public static readonly string SlackToken = @"\bxox[baprs]-[0-9]{10,13}-[0-9]{10,13}-[A-Za-z0-9]{24,32}\b";

        /// <summary>Twilio Auth Token</summary>
        public static readonly string TwilioAuthToken = @"\b[A-Fa-f0-9]{32}\b";
    }

    /// <summary>
    /// JWT and OAuth Tokens
    /// </summary>
    public static class Tokens
    {
        /// <summary>JWT Token pattern</summary>
        public static readonly string Jwt = @"\beyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]*\.[A-Za-z0-9_-]*\b";

        /// <summary>Bearer Token pattern</summary>
        public static readonly string Bearer = @"\bBearer\s+[A-Za-z0-9_-]{20,}\b";

        /// <summary>OAuth Access Token</summary>
        public static readonly string OAuthAccess = @"\baccess_token[=:]\s*['""]?([A-Za-z0-9_-]{20,})['""]?";

        /// <summary>OAuth Refresh Token</summary>
        public static readonly string OAuthRefresh = @"\brefresh_token[=:]\s*['""]?([A-Za-z0-9_-]{20,})['""]?";

        /// <summary>Session Token</summary>
        public static readonly string Session = @"\bsession[_-]?token[=:]\s*['""]?([A-Za-z0-9_-]{20,})['""]?";
    }

    /// <summary>
    /// Cryptographic Keys and Certificates
    /// </summary>
    public static class Crypto
    {
        /// <summary>RSA Private Key</summary>
        public static readonly string RsaPrivateKey = @"-----BEGIN RSA PRIVATE KEY-----[\s\S]*?-----END RSA PRIVATE KEY-----";

        /// <summary>OpenSSH Private Key</summary>
        public static readonly string OpenSshPrivateKey = @"-----BEGIN OPENSSH PRIVATE KEY-----[\s\S]*?-----END OPENSSH PRIVATE KEY-----";

        /// <summary>PGP Private Key</summary>
        public static readonly string PgpPrivateKey = @"-----BEGIN PGP PRIVATE KEY BLOCK-----[\s\S]*?-----END PGP PRIVATE KEY BLOCK-----";

        /// <summary>EC Private Key</summary>
        public static readonly string EcPrivateKey = @"-----BEGIN EC PRIVATE KEY-----[\s\S]*?-----END EC PRIVATE KEY-----";

        /// <summary>PKCS#8 Private Key</summary>
        public static readonly string Pkcs8PrivateKey = @"-----BEGIN PRIVATE KEY-----[\s\S]*?-----END PRIVATE KEY-----";

        /// <summary>X.509 Certificate</summary>
        public static readonly string X509Certificate = @"-----BEGIN CERTIFICATE-----[\s\S]*?-----END CERTIFICATE-----";

        /// <summary>Base64 encoded key (64+ characters)</summary>
        public static readonly string Base64Key = @"\b[A-Za-z0-9+/]{64,}={0,2}\b";
    }

    /// <summary>
    /// Database Connection Strings
    /// </summary>
    public static class Database
    {
        /// <summary>SQL Server connection string with password</summary>
        public static readonly string SqlServerPassword = @"(?i)password\s*=\s*['""]?([^;'""]+)['""]?";

        /// <summary>MySQL connection string with password</summary>
        public static readonly string MySqlPassword = @"(?i)pwd\s*=\s*['""]?([^;'""]+)['""]?";

        /// <summary>PostgreSQL connection string</summary>
        public static readonly string PostgreSqlPassword = @"(?i)password\s*=\s*['""]?([^;&'""]+)['""]?";

        /// <summary>MongoDB connection string</summary>
        public static readonly string MongoDbUri = @"mongodb(?:\+srv)?://[^:]+:([^@]+)@[^/]+";

        /// <summary>Redis connection string with password</summary>
        public static readonly string RedisPassword = @"(?i)password\s*=\s*['""]?([^,;'""]+)['""]?";

        /// <summary>Generic database URL with credentials</summary>
        public static readonly string DatabaseUrl = @"(?i)[a-z]+://[^:/]+:([^@]+)@[^/]+";
    }

    /// <summary>
    /// Cloud Provider Secrets
    /// </summary>
    public static class Cloud
    {
        /// <summary>Azure Storage Account Key</summary>
        public static readonly string AzureStorageKey = @"\b[A-Za-z0-9+/]{88}==\b";

        /// <summary>GCP Service Account Key (JSON)</summary>
        public static readonly string GcpServiceAccountKey = @"""private_key"":\s*""[^""]*""";

        /// <summary>Heroku API Key</summary>
        public static readonly string HerokuApiKey = @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b";

        /// <summary>DigitalOcean Personal Access Token</summary>
        public static readonly string DigitalOceanToken = @"\bdop_v1_[a-f0-9]{64}\b";
    }

    /// <summary>
    /// Application Secrets
    /// </summary>
    public static class Application
    {
        /// <summary>Encryption key pattern</summary>
        public static readonly string EncryptionKey = @"\b[A-Fa-f0-9]{32,128}\b";

        /// <summary>Hash/Salt pattern</summary>
        public static readonly string HashSalt = @"\b[A-Fa-f0-9]{16,64}\b";

        /// <summary>GUID/UUID pattern</summary>
        public static readonly string GuidPattern = @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b";

        /// <summary>Machine Key (ASP.NET)</summary>
        public static readonly string MachineKey = @"\bmachineKey[^>]*validationKey\s*=\s*['""]([A-Fa-f0-9]{128,})['""]";

        /// <summary>View State MAC Key</summary>
        public static readonly string ViewStateMacKey = @"\bdecryptionKey\s*=\s*['""]([A-Fa-f0-9]{48,})['""]";
    }

    /// <summary>
    /// Gets all secret patterns organized by category.
    /// </summary>
    /// <returns>Dictionary of pattern categories and their patterns</returns>
    public static Dictionary<string, Dictionary<string, string>> GetAllPatterns()
    {
        return new Dictionary<string, Dictionary<string, string>>
        {
            ["ApiKeys"] = new()
            {
                ["Generic"] = ApiKeys.Generic,
                ["AwsAccessKey"] = ApiKeys.AwsAccessKey,
                ["AwsSecretKey"] = ApiKeys.AwsSecretKey,
                ["GoogleApiKey"] = ApiKeys.GoogleApiKey,
                ["GitHubToken"] = ApiKeys.GitHubToken,
                ["AzureClientSecret"] = ApiKeys.AzureClientSecret,
                ["StripeApiKey"] = ApiKeys.StripeApiKey,
                ["SendGridApiKey"] = ApiKeys.SendGridApiKey,
                ["SlackToken"] = ApiKeys.SlackToken,
                ["TwilioAuthToken"] = ApiKeys.TwilioAuthToken
            },
            ["Tokens"] = new()
            {
                ["Jwt"] = Tokens.Jwt,
                ["Bearer"] = Tokens.Bearer,
                ["OAuthAccess"] = Tokens.OAuthAccess,
                ["OAuthRefresh"] = Tokens.OAuthRefresh,
                ["Session"] = Tokens.Session
            },
            ["Crypto"] = new()
            {
                ["RsaPrivateKey"] = Crypto.RsaPrivateKey,
                ["OpenSshPrivateKey"] = Crypto.OpenSshPrivateKey,
                ["PgpPrivateKey"] = Crypto.PgpPrivateKey,
                ["EcPrivateKey"] = Crypto.EcPrivateKey,
                ["Pkcs8PrivateKey"] = Crypto.Pkcs8PrivateKey,
                ["X509Certificate"] = Crypto.X509Certificate,
                ["Base64Key"] = Crypto.Base64Key
            },
            ["Database"] = new()
            {
                ["SqlServerPassword"] = Database.SqlServerPassword,
                ["MySqlPassword"] = Database.MySqlPassword,
                ["PostgreSqlPassword"] = Database.PostgreSqlPassword,
                ["MongoDbUri"] = Database.MongoDbUri,
                ["RedisPassword"] = Database.RedisPassword,
                ["DatabaseUrl"] = Database.DatabaseUrl
            },
            ["Cloud"] = new()
            {
                ["AzureStorageKey"] = Cloud.AzureStorageKey,
                ["GcpServiceAccountKey"] = Cloud.GcpServiceAccountKey,
                ["HerokuApiKey"] = Cloud.HerokuApiKey,
                ["DigitalOceanToken"] = Cloud.DigitalOceanToken
            },
            ["Application"] = new()
            {
                ["EncryptionKey"] = Application.EncryptionKey,
                ["HashSalt"] = Application.HashSalt,
                ["GuidPattern"] = Application.GuidPattern,
                ["MachineKey"] = Application.MachineKey,
                ["ViewStateMacKey"] = Application.ViewStateMacKey
            }
        };
    }

    /// <summary>
    /// Gets high-priority secret patterns that should always be detected.
    /// </summary>
    /// <returns>Array of critical secret patterns</returns>
    public static string[] GetCriticalPatterns()
    {
        return new[]
        {
            // Private Keys - Always critical
            Crypto.RsaPrivateKey,
            Crypto.OpenSshPrivateKey,
            Crypto.PgpPrivateKey,
            Crypto.EcPrivateKey,
            Crypto.Pkcs8PrivateKey,
            
            // Cloud Provider Keys
            ApiKeys.AwsAccessKey,
            ApiKeys.AwsSecretKey,
            ApiKeys.GoogleApiKey,
            Cloud.AzureStorageKey,
            Cloud.GcpServiceAccountKey,
            
            // Database Credentials
            Database.SqlServerPassword,
            Database.MySqlPassword,
            Database.PostgreSqlPassword,
            Database.MongoDbUri,
            
            // JWT Tokens
            Tokens.Jwt,
            Tokens.Bearer
        };
    }

    /// <summary>
    /// Gets patterns for property names that typically contain secrets.
    /// </summary>
    /// <returns>Array of property name patterns</returns>
    public static string[] GetSecretPropertyPatterns()
    {
        return new[]
        {
            @".*[Pp]assword.*",
            @".*[Ss]ecret.*",
            @".*[Tt]oken.*",
            @".*[Kk]ey.*",
            @".*[Aa]pi[Kk]ey.*",
            @".*[Aa]ccess[Kk]ey.*",
            @".*[Pp]rivate[Kk]ey.*",
            @".*[Cc]lient[Ss]ecret.*",
            @".*[Aa]uth.*",
            @".*[Cc]redential.*",
            @".*[Cc]onnection[Ss]tring.*",
            @".*[Dd]atabase[Uu]rl.*",
            @".*[Ee]ncryption[Kk]ey.*",
            @".*[Ss]igning[Kk]ey.*",
            @".*[Jj]wt.*",
            @".*[Bb]earer.*",
            @".*[Cc]ertificate.*",
            @".*[Mm]achine[Kk]ey.*",
            @".*[Ss]alt.*",
            @".*[Hh]ash.*"
        };
    }
}

/// <summary>
/// Secret detection result containing the matched pattern and metadata.
/// </summary>
public sealed class SecretDetectionResult
{
    /// <summary>Gets or sets the type of secret detected.</summary>
    public string SecretType { get; set; } = string.Empty;

    /// <summary>Gets or sets the category of the secret (ApiKeys, Tokens, etc.).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets the start position of the match.</summary>
    public int StartPosition { get; set; }

    /// <summary>Gets or sets the length of the matched content.</summary>
    public int Length { get; set; }

    /// <summary>Gets or sets the confidence level of the detection (0.0-1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Gets or sets the severity level of this secret exposure.</summary>
    public SecretSeverity Severity { get; set; }

    /// <summary>Gets or sets additional context about the detection.</summary>
    public string? Context { get; set; }

    /// <summary>Gets or sets whether this secret should be redacted.</summary>
    public bool ShouldRedact { get; set; } = true;
}

/// <summary>
/// Severity levels for secret exposure.
/// </summary>
public enum SecretSeverity
{
    /// <summary>Low severity - might be a false positive.</summary>
    Low = 0,

    /// <summary>Medium severity - likely a secret but not critical.</summary>
    Medium = 1,

    /// <summary>High severity - definitely a secret with security implications.</summary>
    High = 2,

    /// <summary>Critical severity - private keys, production credentials.</summary>
    Critical = 3
}