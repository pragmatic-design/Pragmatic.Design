namespace Pragmatic.Configuration.Aws;

/// <summary>
///     Options for the AWS configuration backends (Secrets Manager for secrets, SSM Parameter Store for
///     configuration).
/// </summary>
public sealed class AwsConfigurationOptions
{
    /// <summary>AWS region (e.g. <c>us-east-1</c>). Falls back to the SDK's default resolution when null.</summary>
    public string? Region { get; set; }

    /// <summary>
    ///     Custom service endpoint (e.g. a LocalStack URL for testing). When set, the client targets this
    ///     endpoint instead of the real AWS endpoints.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Explicit access key. When null the SDK's default credential chain is used.</summary>
    public string? AccessKey { get; set; }

    /// <summary>Explicit secret key. When null the SDK's default credential chain is used.</summary>
    public string? SecretKey { get; set; }

    /// <summary>
    ///     Optional prefix under which all Pragmatic entries live (e.g. <c>pragmatic</c>), so they do not
    ///     collide with other secrets/parameters in the account. Tenant entries nest under <c>tenants/{id}</c>.
    /// </summary>
    public string? PathPrefix { get; set; }
}
