namespace NewsStudio.Domain.Enums;

public enum VideoProjectStatus
{
    Pending = 0,
    Extracting = 1,
    Extracted = 2,
    Analyzing = 3,
    Scripting = 4,
    PlanningScenes = 5,
    GeneratingAudio = 6,
    Rendering = 7,
    QualityChecking = 8,
    AwaitingApproval = 9,
    Publishing = 10,
    Published = 11,
    Failed = 12,
    Cancelled = 13
}
