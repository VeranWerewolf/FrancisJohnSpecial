namespace GameScoreCalculator.Helpers;

public static class MessageBus
{
    private static readonly Dictionary<Type, List<Action<object>>> _subscribers = [];

    public static void Publish<T>(T message)
    {
        if (_subscribers.TryGetValue(typeof(T), out var actions) && message != null)
        {
            foreach (var action in actions)
            {
                action.Invoke(message);
            }
        }
    }

    public static void Subscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (!_subscribers.TryGetValue(type, out List<Action<object>>? value))
        {
            value = ([]);
            _subscribers[type] = value;
        }

        value.Add(m => handler((T)m));
    }
}