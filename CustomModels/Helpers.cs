using System;
using System.Collections.Generic;
using System.Linq;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {

        private static string GetNameWithPlus(string name) {
            return name.EndsWith("+") ? name : name + "+";
        }

        private static string GetNameWithoutPlus(string name) {
            return name.EndsWith("+") ? name.Substring(0, name.Length - 1) : name;
        }

        static string ListicleNumber(int n) {
            if (n > 3 && n < 21) { return n + "th"; }
            string suffix;
            switch (n % 10) {
                case 1:
                    suffix = "st";
                    break;
                case 2:
                    suffix = "nd";
                    break;
                case 3:
                    suffix = "rd";
                    break;
                default:
                    suffix = "th";
                    break;
            }
            return n + suffix;
        }

        private static void Swap<T>(ref T lhs, ref T rhs) {
            T temp = lhs;
            lhs = rhs;
            rhs = temp;
        }

        private static readonly bool debug = true;
        private static void Debug(string format, object arg0, object arg1, object arg2) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format, arg0, arg1, arg2);
        }
        private static void Debug(string format, object arg0, object arg1) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format, arg0, arg1);
        }
        private static void Debug(string format, object arg0) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format, arg0);
        }
        private static void Debug(string format) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format);
        }

    }


    static class ListExtension {
        public static T PopFront<T>(this List<T> list) {
            T r = list[0];
            list.RemoveAt(0);
            return r;
        }

        public static T PopBack<T>(this List<T> list) {
            T r = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return r;
        }

        public static IEnumerable<List<T>> Partition<T>(this IList<T> source, int size) {
            for (int i = 0; i < Math.Ceiling(source.Count / (double)size); i++) {
                yield return new List<T>(source.Skip(size * i).Take(size));
            }
        }
    }

} // namespace MCGalaxy
