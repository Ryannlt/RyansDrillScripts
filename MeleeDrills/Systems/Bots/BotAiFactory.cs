using System;
using System.Collections.Generic;

// Creates bot AI instances by type. Mirrors the registry pattern, but since its job is construction
// it's named as a factory. Returns fresh instances (not shared singletons) because an AI may hold
// per-bot state (timers, etc). UMod disallows reflection, so AI types are registered manually.

namespace MDS.Systems
{
    public static class BotAiFactory
    {
        private static readonly Dictionary<BotAiEnum, Func<IBotAi>> _factories = new();

        static BotAiFactory()
        {
            Register(BotAiEnum.None, () => new NoneAi());
            Register(BotAiEnum.Manual, () => new ManualAi());

            Logger.Log($"Registered {_factories.Count} bot AI type(s).", LogLevel.INFO);
        }

        public static void Register(BotAiEnum type, Func<IBotAi> factory)
        {
            _factories[type] = factory;
        }

        public static bool IsRegistered(BotAiEnum type) => _factories.ContainsKey(type);

        // Returns a fresh AI instance for the given type, falling back to Idle if unregistered.
        public static IBotAi Create(BotAiEnum type)
        {
            if (_factories.TryGetValue(type, out var factory))
            {
                return factory();
            }

            Logger.Log($"No AI registered for '{type}'. Falling back to Idle.", LogLevel.WARNING);
            return new NoneAi();
        }
    }
}
