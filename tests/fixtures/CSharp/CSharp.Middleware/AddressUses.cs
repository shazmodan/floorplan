namespace CSharp.Middleware;

/// Many calls on one struct local in a method that reads request data: each call takes the local's address, and
/// each address is added to the local's values, so the request-data analysis must not walk them again from every
/// later one (exponential before; a real solution's async methods hung on it). Registered nowhere.
public static class AddressUses
{
    public static string Many(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        var text = path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        text += path.ToString(); text += path.ToString(); text += path.ToString(); text += path.ToString();
        // Passing the local to a user method asks for its tags, through every address taken above.
        return text + Describe(path);
    }

    private static string Describe(PathString path) => path.ToString();
}
