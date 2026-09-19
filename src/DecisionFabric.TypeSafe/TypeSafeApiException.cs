using System.Net;

namespace DecisionFabric.TypeSafe;

public sealed class TypeSafeApiException(
    HttpStatusCode statusCode,
    string responseBody)
    : Exception($"TypeSafe API returned HTTP {(int)statusCode} ({statusCode}).")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
