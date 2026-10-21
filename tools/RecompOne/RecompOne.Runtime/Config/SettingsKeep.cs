using System.Text.Json.Nodes;

namespace RecompOne.Runtime.Config;

/// <summary>
/// A newer build adds settings. Values already in the player's file stay,
/// including names this build does not know.
/// </summary>
public static class SettingsKeep
{
    /// <summary>True when <paramref name="defaults"/> has a property
    /// <paramref name="player"/> does not, at any depth. An array or a
    /// scalar the player already set is theirs, whole.</summary>
    public static bool MissingAny(JsonObject player, JsonObject defaults)
    {
        foreach (var pair in defaults)
        {
            if (pair.Value is null) continue;
            if (!player.TryGetPropertyValue(pair.Key, out var existing) || existing is null)
                return true;
            if (existing is JsonObject have && pair.Value is JsonObject want && MissingAny(have, want))
                return true;
        }

        return false;
    }

    /// <summary>Copy properties from <paramref name="existing"/> that
    /// <paramref name="current"/> does not name. Nested objects are walked
    /// the same way. A value <paramref name="current"/> already has is kept.</summary>
    public static void KeepUnknown(JsonObject current, JsonObject existing)
    {
        foreach (var pair in existing)
        {
            if (!current.TryGetPropertyValue(pair.Key, out var have) || have is null)
            {
                if (pair.Value is not null)
                    current[pair.Key] = pair.Value.DeepClone();
                continue;
            }

            if (have is JsonObject haveObj && pair.Value is JsonObject existingObj)
                KeepUnknown(haveObj, existingObj);
        }
    }
}
