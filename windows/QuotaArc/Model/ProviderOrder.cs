namespace QuotaArc.Model;

internal static class ProviderOrder
{
    public static List<T> Arrange<T>(IReadOnlyList<T> items, IReadOnlyList<string> order, Func<T, string> id)
    {
        if (order.Count == 0) return [.. items];

        var remaining = items.ToList();
        var arranged = new List<T>();
        foreach (var providerId in order)
        {
            var index = remaining.FindIndex(item => id(item) == providerId);
            if (index < 0) continue;
            arranged.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        arranged.AddRange(remaining);
        return arranged;
    }

    public static List<string> JoiningConnected(string id, IReadOnlyList<string> order, Func<string, bool> isConnected)
    {
        var rest = order.Where(existing => existing != id).ToList();
        var insertAt = 0;
        for (var i = 0; i < rest.Count; i++)
        {
            if (isConnected(rest[i])) insertAt = i + 1;
        }
        rest.Insert(insertAt, id);
        return rest;
    }

    public static List<string> Remember(IReadOnlyList<string> visible, IReadOnlyList<string> remembered) =>
        [.. visible, .. remembered.Where(id => !visible.Contains(id))];
}
