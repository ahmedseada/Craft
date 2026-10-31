// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;

namespace Craft.Tests.Runner;

/// <summary>A failed check: the test fails with its message.</summary>
public sealed class CheckFailedException(string message) : Exception(message);

/// <summary>The checks a test makes; each failure names what was expected and what came.</summary>
public static class Check
{
    /// <summary>Fails unless <paramref name="condition"/> holds.</summary>
    public static void True(bool condition, string because, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
        {
            throw new CheckFailedException($"{because} ({expression} was false)");
        }
    }

    /// <summary>Fails unless <paramref name="actual"/> equals <paramref name="expected"/>.</summary>
    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new CheckFailedException($"{what}: expected {Show(expected)}, got {Show(actual)}");
        }
    }

    /// <summary>Fails unless the two sequences are equal item by item, naming the first difference.</summary>
    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string what)
    {
        int n = Math.Min(expected.Count, actual.Count);
        for (int i = 0; i < n; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
            {
                throw new CheckFailedException($"{what}: item {i} expected {Show(expected[i])}, got {Show(actual[i])}");
            }
        }

        if (expected.Count != actual.Count)
        {
            throw new CheckFailedException($"{what}: expected {expected.Count} items, got {actual.Count}");
        }
    }

    /// <summary>Fails unless <paramref name="action"/> throws a <typeparamref name="TException"/>; returns it.</summary>
    public static TException Throws<TException>(Action action, string what) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException e)
        {
            return e;
        }
        catch (Exception e)
        {
            throw new CheckFailedException($"{what}: expected {typeof(TException).Name}, got {e.GetType().Name}: {e.Message}");
        }

        throw new CheckFailedException($"{what}: expected {typeof(TException).Name}, nothing was thrown");
    }

    private static string Show<T>(T value) => value switch
    {
        null => "null",
        string s => $"\"{(s.Length > 200 ? s[..200] + "…" : s).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"",
        _ => value.ToString() ?? "",
    };
}
