using Application.Common.Interfaces;

namespace Cable.Ocpp.Services;

/// <summary>
/// The audit interceptor and MediatR behaviours expect a current user; in this
/// process there is none — rows written by Cable.Ocpp carry CreatedBy = null,
/// which is how the API tells "the charger did it" from "an admin did it".
/// </summary>
public sealed class OcppSystemUserService : ICurrentUserService
{
    public int? UserId => null;
    public string Token => "";
    public string App => "ocpp";
}
