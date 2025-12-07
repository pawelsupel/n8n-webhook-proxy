namespace WebhookProxy.Options;

public sealed class ValidationOptions
{
    public string BasePath { get; set; } = "validations";
    public string Mode { get; set; } = "permissive"; // permissive | strict
}
