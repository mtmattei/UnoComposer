namespace Composer.Models;

public partial record Intent(
    string AppType,
    string PrimaryUser,
    string Workflow,
    string Platforms)
{
    public static Intent Example { get; } = new(
        AppType: "Field-service scheduling",
        PrimaryUser: "Mobile-first technicians",
        Workflow: "Receive jobs, schedule, dispatch",
        Platforms: "Web, iOS, Android");

    public static Intent Empty { get; } = new("", "", "", "");

    public bool MatchesExample()
        => AppType == Example.AppType
        && PrimaryUser == Example.PrimaryUser
        && Workflow == Example.Workflow
        && Platforms == Example.Platforms;

}
