using System.Net.Http;
using System.Net.Quic;
using System.Net.Sockets;
using System.Security.Authentication;

namespace LightDl.UI.Services;

public static class Http3FailurePolicy
{
    // Storage failures, HTTP status errors and certificate failures will not be
    // repaired by changing protocol. Inspect typed causes, not localized messages.
    public static bool CanOfferFallback(Exception error)
    {
        if (error is AggregateException aggregate)
            return aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(CanOfferFallback);
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
        {
            if (cause is AuthenticationException ||
                cause is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } ||
                cause is HttpRequestException { StatusCode: not null })
                return false;
        }
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
        {
            if (cause is PlatformNotSupportedException or QuicException or SocketException or
                TimeoutException or OperationCanceledException)
                return true;
            if (cause is HttpRequestException request && request.HttpRequestError is
                HttpRequestError.Unknown or HttpRequestError.VersionNegotiationError or
                HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or
                HttpRequestError.HttpProtocolError or HttpRequestError.ResponseEnded)
                return true;
            if (cause is HttpIOException { HttpRequestError: HttpRequestError.HttpProtocolError or HttpRequestError.ResponseEnded })
                return true;
        }
        return false;
    }
}
