namespace Ansight.Skills;

public interface ISkill
{
    string Category { get; }
    
    string Id { get; }
    
    string Name { get; }
    
    string Description { get; }
    
    string Keywords { get; }

    Task<SkillResult> Execute(IReadOnlyDictionary<string, string> arguments);
}