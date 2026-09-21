using System.Text.Json.Serialization;

namespace AstraGraph.Binding;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SecurityProfile
{
    Gameplay,
    Trusted,
    Engine
}

/// <summary>
/// Controls authorization and accessibility of native bindings according to security profiles.
/// </summary>
public static class AccessPolicy
{
    public static bool IsAccessAllowed(SecurityProfile methodProfile, SecurityProfile callerProfile)
    {
        // Engine profile has full access
        if (callerProfile == SecurityProfile.Engine) return true;

        // Trusted profile can access Gameplay and Trusted
        if (callerProfile == SecurityProfile.Trusted)
        {
            return methodProfile is SecurityProfile.Gameplay or SecurityProfile.Trusted;
        }

        // Gameplay profile can only access Gameplay methods
        return methodProfile == SecurityProfile.Gameplay;
    }
}
