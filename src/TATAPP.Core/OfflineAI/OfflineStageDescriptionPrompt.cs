namespace TATAPP.Core.OfflineAI;

public static class OfflineStageDescriptionPrompt
{
    public static string BuildOriginal(TattooStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (stage.Kind != TattooStageKind.Original)
            throw new ArgumentException(
                "Offline model inference is permitted only for the Original image stage.",
                nameof(stage));
        return
            $"Describe only objective visible facts in the rendered TATAPP stage '{stage.Name}'. " +
            $"The deterministic renderer defines this stage as: {stage.Description} " +
            "Describe the original source exactly as visible in this rendering. Do not compare it with another stage or claim any transformation. " +
            "This is a design-only source image, not an anatomical placement view; do not mention a body, body location, or anatomical placement. " +
            "Distinguish uncertainty, avoid identity or medical claims, and do not infer unseen content. " +
            "Return one short paragraph only, with no headings, lists, quotations, code blocks, or alternative answers.";
    }
}
