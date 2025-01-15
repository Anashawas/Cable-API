namespace Application.Common.Models;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class ApplicationAuthorizeAttribute : Attribute
{
    public string PrivilegeCode { get; set; }

    public ApplicationAuthorizeAttribute()
    {
    }
}
