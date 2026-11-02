using System.Collections.ObjectModel;

namespace VaultManager.App.Logic;

internal static class CollectionSync
{
	/// <summary>
	/// Makes a bound list match the wanted one while keeping every row that survives.
	/// A row kept is a row whose focus, hover and scroll position stay where they were.
	/// </summary>
	public static void Sync<TRow, TData>(
		ObservableCollection<TRow> rows,
		IReadOnlyList<TData> wanted,
		Func<TRow, string> rowKey,
		Func<TData, string> dataKey,
		Func<TData, TRow> create,
		Action<TRow, TData> update)
	{
		var keep = new HashSet<string>(wanted.Select(dataKey), StringComparer.Ordinal);
		for (int i = rows.Count - 1; i >= 0; i--)
		{
			if (!keep.Contains(rowKey(rows[i])))
			{
				rows.RemoveAt(i);
			}
		}

		for (int i = 0; i < wanted.Count; i++)
		{
			string key = dataKey(wanted[i]);
			int at = -1;
			for (int j = i; j < rows.Count; j++)
			{
				if (rowKey(rows[j]) == key)
				{
					at = j;
					break;
				}
			}

			if (at < 0)
			{
				rows.Insert(i, create(wanted[i]));
				continue;
			}

			if (at != i)
			{
				rows.Move(at, i);
			}

			update(rows[i], wanted[i]);
		}
	}
}
