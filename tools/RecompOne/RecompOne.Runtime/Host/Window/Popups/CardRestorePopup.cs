using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

public sealed class CardRestorePopup : Popup
{
    protected override string TitleKey => "cards.restore.title";
    protected override Vector2 Size => new(480f, 0f);

    protected override void DrawContent()
    {
        var names = string.Join(", ", CardFiles.Pending.Select(p => p.Name));
        UiText.CenteredWrapped(Localization.T("cards.restore.body", CardFiles.PendingFolder, names));

        if (CardFiles.PendingSkipped.Count > 0)
        {
            ImGui.Spacing();
            UiText.CenteredWrapped(Localization.T("cards.restore.bad", string.Join(", ", CardFiles.PendingSkipped)));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var width = 140f;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var row = width * 2f + gap;
        ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - row) * 0.5f + ImGui.GetCursorPosX());
        if (ImGui.Button(Localization.T("cards.restore.confirm"), new Vector2(width, 0f)))
        {
            Close();
            CardFiles.CommitRestore();
        }

        ImGui.SameLine();
        if (ImGui.Button(Localization.T("cards.restore.cancel"), new Vector2(width, 0f)))
            Close();
    }
}
