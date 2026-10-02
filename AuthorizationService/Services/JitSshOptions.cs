namespace AuthorizationService.Services;

/// <summary>
/// Configuration for the SSH credential the brokered terminal uses to reach a VM.
///
/// The broker can log in two ways, and this is the fix for "it only works for
/// azureuser":
///
///   1. As the JIT session's own user. Azure provisions a Linux account per
///      Entra identity on AAD-SSH-enabled VMs (the account is literally named
///      <c>user@gmail.com</c>), and that account owns its own key pair. When the
///      session carries a user email, the broker logs in AS THAT USER so the
///      shell, the audit trail and the VM's own authorization all agree with the
///      <c>ssh user@gmail@vm</c> command shown in the UI.
///   2. As a shared fallback account (<see cref="Username"/>, historically
///      <c>azureuser</c>), using the single held key. Used when the session has
///      no resolvable email, or when no per-user key is registered for it.
///
/// Credentials are never returned to the browser. Supply them via env vars so
/// the key never lands in source control: <c>JitSsh__PrivateKey</c>,
/// <c>JitSsh__PrivateKeyPath</c>, <c>JitSsh__PrivateKeys__user@x.com</c>, ...
/// </summary>
public sealed class JitSshOptions
{
    public const string SectionName = "JitSsh";

    /// <summary>Placeholder replaced with the session's user email in <see cref="UsernameTemplate"/>.</summary>
    public const string EmailPlaceholder = "{email}";

    /// <summary>PEM private key used as the fallback credential.</summary>
    public string? PrivateKey { get; init; }

    /// <summary>
    /// Path to a PEM file on disk, used as the fallback credential. Preferred
    /// over <see cref="PrivateKey"/> in containers: mount the key as a secret and
    /// point at it, which sidesteps the newline-escaping problems of passing a
    /// PEM through an environment variable.
    /// </summary>
    public string? PrivateKeyPath { get; init; }

    /// <summary>Passphrase for an encrypted private key, when one is used.</summary>
    public string? Passphrase { get; init; }

    /// <summary>
    /// Per-user private keys, keyed by SSH login (case-insensitive). Each value
    /// is either inline PEM or a path to a PEM file - a value containing
    /// <c>-----BEGIN</c> is treated as inline PEM, anything else as a file path.
    /// Bind from env vars as <c>JitSsh__PrivateKeys__user@example.com</c>.
    /// </summary>
    public Dictionary<string, string> PrivateKeys { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fallback SSH login when the session has no resolvable email. Historically
    /// <c>azureuser</c>; still honoured as a fallback so existing deployments
    /// keep working.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Directory holding one private key file per SSH login, named after the
    /// login (e.g. <c>keys/user@example.com</c>). This is the practical way to
    /// supply per-user keys in a container: an orchestrator can mount a volume,
    /// but it cannot synthesise one environment variable name per user.
    /// </summary>
    public string? PrivateKeyDirectory { get; init; }

    /// <summary>
    /// Optional login template, e.g. <c>{email}</c>. When set it is used INSTEAD
    /// of the raw email, which is what lets the broker target accounts whose name
    /// is not the UPN (for example <c>{email}@pam</c> or a stripped local part).
    /// </summary>
    public string? UsernameTemplate { get; init; }

    public int Port { get; init; } = 22;

    /// <summary>Bound on the SSH handshake so a hung TCP connect cannot pin a browser tab open.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 20;

    /// <summary>
    /// Optional SHA256 fingerprint of the VM host key (as printed by
    /// <c>ssh-keygen -lf</c>, e.g. <c>SHA256:abc123...</c>). When set, the broker
    /// refuses to complete a connection unless the server presents exactly this
    /// key - this is what stops a DNS/IP hijack from silently capturing the
    /// session. When empty the host key is accepted without pinning.
    /// </summary>
    public string? HostKeyFingerprint { get; init; }

    /// <summary>True when at least one usable credential has been supplied.</summary>
    public bool IsConfigured =>
        HasFallbackCredential || PrivateKeys.Any(pair => !string.IsNullOrWhiteSpace(pair.Value));

    /// <summary>True when a single shared credential is available for the fallback account.</summary>
    public bool HasFallbackCredential =>
        !string.IsNullOrWhiteSpace(PrivateKey) || !string.IsNullOrWhiteSpace(PrivateKeyPath);

    /// <summary>True when a per-user key could be resolved for this login.</summary>
    public bool HasPerUserCredential(string? login)
    {
        if (PrivateKeys.Any(pair => !string.IsNullOrWhiteSpace(pair.Value)))
            return true;

        return ResolveKeyDirectoryPath(login) is not null;
    }

    /// <summary>
    /// Finds the key file in <see cref="PrivateKeyDirectory"/> for a login,
    /// rejecting any name that tries to escape the directory.
    /// </summary>
    private string? ResolveKeyDirectoryPath(string? login)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(PrivateKeyDirectory))
            return null;

        var directory = PrivateKeyDirectory.Trim();
        if (!Directory.Exists(directory))
            return null;

        // Path.GetFileName strips any directory separators, so a login containing
        // "../" can only ever resolve to a bare filename inside the directory.
        var safeName = Path.GetFileName(login.Trim());
        if (string.IsNullOrWhiteSpace(safeName))
            return null;

        var candidate = Path.Combine(directory, safeName);
        return File.Exists(candidate) ? candidate : null;
    }

    // Resolution helpers continue below.

    /// <summary>
    /// Resolves the SSH login for a JIT session.
    ///
    /// Template first (when configured), then the session's own email - which is
    /// the account Azure actually created on the VM - and only then the shared
    /// fallback username. Returns null when nothing usable is available.
    /// </summary>
    public string? ResolveLogin(string? userEmail)
    {
        var email = userEmail?.Trim();

        if (!string.IsNullOrWhiteSpace(UsernameTemplate))
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            var resolved = UsernameTemplate
                .Replace(EmailPlaceholder, email, StringComparison.OrdinalIgnoreCase)
                .Trim();
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }

        if (!string.IsNullOrWhiteSpace(email))
            return email;

        return string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
    }

    /// <summary>
    /// Resolves the private key for a login: the per-user key when one is
    /// registered, otherwise the shared fallback credential. Returns false when
    /// no key is available for this login.
    /// </summary>
    public bool TryResolveKey(string? login, out string? pem, out string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(login))
        {
            var trimmed = login.Trim();

            // Explicit per-user mapping wins, then a key file named after the
            // login in the mounted directory, then the shared fallback key.
            if (PrivateKeys.TryGetValue(trimmed, out var mapped)
                && !string.IsNullOrWhiteSpace(mapped))
            {
                return SplitKeyValue(mapped, out pem, out filePath);
            }

            var fromDirectory = ResolveKeyDirectoryPath(trimmed);
            if (fromDirectory is not null)
            {
                pem = null;
                filePath = fromDirectory;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(PrivateKey))
        {
            pem = NormalizePem(PrivateKey);
            filePath = null;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(PrivateKeyPath))
        {
            pem = null;
            filePath = PrivateKeyPath.Trim();
            return true;
        }

        pem = null;
        filePath = null;
        return false;
    }

    /// <summary>
    /// Normalizes a configured fingerprint for comparison with SSH.NET's
    /// base64 (unpadded) SHA256 representation. Accepts the
    /// <c>SHA256:prefix</c> form printed by <c>ssh-keygen -lf</c>.
    /// </summary>
    public static string? NormalizeFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        var value = fingerprint.Trim();
        var separator = value.IndexOf(':');
        if (separator >= 0)
            value = value[(separator + 1)..];

        return value.Trim().TrimEnd('=').ToLowerInvariant();
    }

    private static bool SplitKeyValue(string value, out string? pem, out string? filePath)
    {
        if (value.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            pem = NormalizePem(value);
            filePath = null;
            return true;
        }

        pem = null;
        filePath = value.Trim();
        return true;
    }

    /// <summary>
    /// Repairs PEM text that has been through an environment variable, where
    /// newlines usually arrive as the two-character sequence <c>\n</c> rather than
    /// a real line feed, and normalizes CRLF to LF.
    /// </summary>
    private static string NormalizePem(string value)
    {
        var text = value.Replace("\\r\\n", "\n", StringComparison.Ordinal)
                        .Replace("\\n", "\n", StringComparison.Ordinal)
                        .Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Replace("\r", "\n", StringComparison.Ordinal);
        return text.Trim();
    }
}