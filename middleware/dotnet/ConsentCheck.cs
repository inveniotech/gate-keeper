// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.RegularExpressions;

namespace InvenioTech.GateKeeper.Middleware;

/// <summary>The three consent answers; missing records are the provider's denied answer.</summary>
public enum ConsentState
{
    /// <summary>The provider confirms consent for the requested purpose.</summary>
    Granted,
    /// <summary>The provider does not hold the required consent.</summary>
    Denied,
    /// <summary>No reliable answer could be obtained.</summary>
    Unknown,
}

/// <summary>An application-owned, concurrency-safe consent provider.</summary>
public interface IConsentProvider
{
    /// <summary>Answers without the adapter retaining records or adding retries.</summary>
    /// <param name="subjectId">The opaque application subject, never logged by the adapter.</param>
    /// <param name="purpose">The validated namespaced purpose.</param>
    /// <param name="cancellationToken">Caller cancellation, forwarded unchanged.</param>
    ValueTask<ConsentState> GetStateAsync(string subjectId, string purpose, CancellationToken cancellationToken);
}

/// <summary>A safe diagnostic containing no subject, exception message or stack trace.</summary>
/// <param name="Purpose">The requested purpose, or null for the absent-provider start-up warning.</param>
/// <param name="ErrorType">Only the fault type name.</param>
public sealed record ConsentDiagnostic(string? Purpose, string ErrorType);

/// <summary>A stateless tri-state helper; it never changes matching or redaction.</summary>
public sealed class ConsentCheck
{
    private static readonly Regex Purpose = new(@"\A[a-z][a-z0-9-]{0,31}(\.[a-z][a-z0-9-]{0,31}){1,4}\z", RegexOptions.NonBacktracking);
    private readonly IConsentProvider? provider;
    private readonly Action<ConsentDiagnostic> diagnostic;

    /// <summary>Stores only the provider and diagnostic callbacks, never consent answers.</summary>
    /// <param name="provider">Application provider; absence yields unknown and one warning.</param>
    /// <param name="diagnostic">Receives purpose/type metadata only.</param>
    public ConsentCheck(IConsentProvider? provider, Action<ConsentDiagnostic> diagnostic)
    {
        this.provider = provider; this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        if (provider is null) diagnostic(new ConsentDiagnostic(null, "NoConsentProvider"));
    }

    /// <summary>Returns a provider answer or unknown on faults; propagates cancellation.</summary>
    /// <param name="subjectId">An opaque subject of 1 to 1,024 Unicode scalars.</param>
    /// <param name="purpose">Two to five namespaced ASCII segments, at most 164 characters.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    public async ValueTask<ConsentState> CheckAsync(string subjectId, string purpose, CancellationToken cancellationToken = default)
    {
        if (subjectId is null || subjectId.Length is 0 or > 2048) throw new ArgumentException("Supply a subject of 1 to 1,024 scalars.", nameof(subjectId));
        RuntimeMatcher.ValidateText(subjectId, nameof(subjectId));
        if (subjectId.EnumerateRunes().Count() > 1024) throw new ArgumentException("Supply a subject of 1 to 1,024 scalars.", nameof(subjectId));
        if (purpose is null || purpose.Length > 164 || !Purpose.IsMatch(purpose))
            throw new ArgumentException("Supply a valid namespaced consent purpose.", nameof(purpose));
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null) return ConsentState.Unknown;
        try
        {
            var state = await provider.GetStateAsync(subjectId, purpose, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return state is ConsentState.Granted or ConsentState.Denied or ConsentState.Unknown ? state : ConsentState.Unknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostic(new ConsentDiagnostic(purpose, error.GetType().Name));
            return ConsentState.Unknown;
        }
    }

    /// <summary>Runs the same check; only granted becomes true.</summary>
    /// <param name="subjectId">The opaque subject.</param>
    /// <param name="purpose">The requested purpose.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    public async ValueTask<bool> IsGrantedAsync(string subjectId, string purpose, CancellationToken cancellationToken = default)
        => await CheckAsync(subjectId, purpose, cancellationToken).ConfigureAwait(false) == ConsentState.Granted;
}
