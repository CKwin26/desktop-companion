namespace DesktopCompanion.WpfHost.Models;

public enum ProjectArchetype
{
    General = 0,
    EngineeringExecution = 1,
    ResearchEvaluation = 2,
    ApplicationOps = 3,
    ProductResearch = 4,
    OperationsAdmin = 5,
    LifeEntertainment = 6
}

public static class ProjectArchetypes
{
    public static string ToLabel(ProjectArchetype archetype)
    {
        return archetype switch
        {
            ProjectArchetype.EngineeringExecution => "engineering_execution",
            ProjectArchetype.ResearchEvaluation => "research_evaluation",
            ProjectArchetype.ApplicationOps => "application_ops",
            ProjectArchetype.ProductResearch => "product_research",
            ProjectArchetype.OperationsAdmin => "operations_admin",
            ProjectArchetype.LifeEntertainment => "life_entertainment",
            _ => "general"
        };
    }

    public static ProjectArchetype ParseLabel(string? label)
    {
        return (label ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "engineering_execution" => ProjectArchetype.EngineeringExecution,
            "research_evaluation" => ProjectArchetype.ResearchEvaluation,
            "application_ops" => ProjectArchetype.ApplicationOps,
            "product_research" => ProjectArchetype.ProductResearch,
            "operations_admin" => ProjectArchetype.OperationsAdmin,
            "life_entertainment" => ProjectArchetype.LifeEntertainment,
            _ => ProjectArchetype.General
        };
    }

    public static string ToDisplayLabel(ProjectArchetype archetype)
    {
        return archetype switch
        {
            ProjectArchetype.EngineeringExecution => "代码实现类",
            ProjectArchetype.ResearchEvaluation => "研究评估类",
            ProjectArchetype.ApplicationOps => "申请写作类",
            ProjectArchetype.ProductResearch => "产品设计类",
            ProjectArchetype.OperationsAdmin => "运营事务类",
            ProjectArchetype.LifeEntertainment => "生活文娱类",
            _ => "暂未定类"
        };
    }

    public static string ToDisplayLabel(string? label)
    {
        return ToDisplayLabel(ParseLabel(label));
    }
}
