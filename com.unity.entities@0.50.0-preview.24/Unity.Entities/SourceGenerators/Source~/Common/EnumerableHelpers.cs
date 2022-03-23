﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Entities.SourceGen.Common
{
    public static class EnumerableHelpers
    {
        public static string SeparateByDot(this IEnumerable<string> lines) => string.Join(".", lines.Where(s => !string.IsNullOrEmpty(s)));
        public static string SeparateByComma(this IEnumerable<string> lines) => string.Join(",", lines.Where(s => !string.IsNullOrEmpty(s)));
        public static string SeparateByCommaAndSpace(this IEnumerable<string> lines) => string.Join(", ", lines.Where(s => !string.IsNullOrEmpty(s)));
        public static string SeparateByBinaryOr(this IEnumerable<string> lines) => string.Join("|", lines.Where(s => !string.IsNullOrEmpty(s)));
        public static string SeparateByCommaAndNewLine(this IEnumerable<string> lines) => string.Join(",\r\n", lines.Where(s => !string.IsNullOrEmpty(s)));
        public static string SeparateByNewLine(this IEnumerable<string> lines) => string.Join("\r\n", lines.Where(s => !string.IsNullOrEmpty(s)));
        public static string SeparateBySemicolonAndNewLine(this IEnumerable<string> things) => string.Join(";\r\n", things.Where(s => !string.IsNullOrEmpty(s)));
        public static string JoinAttributes(this IEnumerable<string> attributes) => string.Join("", attributes.Where(s => !string.IsNullOrEmpty(s)).Select(s => $"[{s}] "));

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            var knownKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (knownKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        public static IEnumerable<TSource> FindDuplicatesBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            var knownKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (!knownKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        public static bool IsStructurallyEqualTo<T>(this IEnumerable<T> first, IEnumerable<T> second)
        {
            return !first.Except(second).Any() && !second.Except(first).Any();
        }
    }
}
