using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VaultManager.App.Controls;

public enum StateKind { None, Match, Older, Off, Blocked, Offline, Waiting, Selection, Review, Warning, Online }

/// <summary>
/// The state marks, drawn as shapes of one size: as font glyphs they fall back to a different
/// font per symbol and come out different sizes. Every state has a shape as well as a colour.
/// </summary>
public sealed class StateGlyph : Control
{
	public static readonly StyledProperty<StateKind> KindProperty = AvaloniaProperty.Register<StateGlyph, StateKind>(nameof(Kind));

	static StateGlyph() => AffectsRender<StateGlyph>(KindProperty);

	public StateGlyph()
	{
		Width = 12;
		Height = 12;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
	}

	public StateKind Kind
	{
		get => GetValue(KindProperty);
		set => SetValue(KindProperty, value);
	}

	/// <summary>The theme token that colours a state. Tests read this to check every state has one.</summary>
	public static string TokenOf(StateKind kind) => kind switch
	{
		StateKind.Match => "Brush.Match",
		StateKind.Older => "Brush.Drift",
		StateKind.Review => "Brush.Drift",
		StateKind.Off => "Brush.Dim",
		StateKind.Blocked => "Brush.Blocked",
		StateKind.Warning => "Brush.Blocked",
		StateKind.Offline => "Brush.Info",
		StateKind.Online => "Brush.Info",
		StateKind.Waiting => "Brush.Soft",
		StateKind.Selection => "Brush.Sulfur",
		_ => "Brush.Dim",
	};

	public override void Render(DrawingContext context)
	{
		if (!this.TryFindResource(TokenOf(Kind), ActualThemeVariant, out object? found) || found is not IBrush brush)
		{
			return;
		}

		var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
		const double radius = 4.5;
		var line = new Pen(brush, 1.5);
		switch (Kind)
		{
			case StateKind.Match:
				context.DrawEllipse(brush, null, center, radius, radius);
				break;
			case StateKind.Older:
				context.DrawEllipse(null, line, center, radius, radius);
				context.DrawGeometry(brush, null, HalfDisc(center, radius));
				break;
			case StateKind.Off:
				context.DrawEllipse(null, line, center, radius, radius);
				break;
			case StateKind.Blocked:
				context.DrawLine(line, center + new Point(-4, -4), center + new Point(4, 4));
				context.DrawLine(line, center + new Point(-4, 4), center + new Point(4, -4));
				break;
			case StateKind.Offline:
				context.DrawEllipse(null, new Pen(brush, 1.5, new DashStyle([1.6, 1.6], 0)), center, radius, radius);
				break;
			case StateKind.Waiting:
				context.DrawEllipse(null, line, center, radius, radius);
				context.DrawLine(line, center, center + new Point(0, -3));
				context.DrawLine(line, center, center + new Point(2.2, 1.2));
				break;
			case StateKind.Selection:
				context.FillRectangle(brush, new Rect(center.X - 3, center.Y - 3, 6, 6));
				break;
			case StateKind.Review:
				context.DrawGeometry(null, line, Triangle(center));
				break;
			case StateKind.Warning:
				context.DrawGeometry(null, line, Triangle(center));
				context.DrawLine(line, center + new Point(0, -1.2), center + new Point(0, 1.6));
				context.FillRectangle(brush, new Rect(center.X - 0.75, center.Y + 2.4, 1.5, 1.5));
				break;
			case StateKind.Online:
				context.DrawLine(line, center + new Point(0, 4.5), center + new Point(0, -4));
				context.DrawLine(line, center + new Point(0, -4.5), center + new Point(-3.5, -1));
				context.DrawLine(line, center + new Point(0, -4.5), center + new Point(3.5, -1));
				break;
			default:
				context.FillRectangle(brush, new Rect(center.X - 1, center.Y - 1, 2, 2));
				break;
		}
	}

	static StreamGeometry HalfDisc(Point center, double radius)
	{
		var geometry = new StreamGeometry();
		using StreamGeometryContext g = geometry.Open();
		g.BeginFigure(center + new Point(0, -radius), true);
		g.ArcTo(center + new Point(0, radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
		g.EndFigure(true);
		return geometry;
	}

	static StreamGeometry Triangle(Point center)
	{
		var geometry = new StreamGeometry();
		using StreamGeometryContext g = geometry.Open();
		g.BeginFigure(center + new Point(0, -5), false);
		g.LineTo(center + new Point(5, 4.5));
		g.LineTo(center + new Point(-5, 4.5));
		g.EndFigure(true);
		return geometry;
	}
}
