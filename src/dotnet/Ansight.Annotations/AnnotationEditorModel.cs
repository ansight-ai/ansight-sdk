namespace Ansight.Annotations;

internal enum AnnotationDrawingTool
{
    Select,
    Rectangle,
    Ellipse,
    FreeDraw
}

internal sealed class AnnotationEditorModel
{
    private const double MinimumShapeSize = 0.005;
    private const double MinimumPathPointDistance = 0.0015;

    private readonly List<AnnotationShape> shapes = [];
    private readonly Stack<IReadOnlyList<AnnotationShape>> undoHistory = [];
    private AnnotationDrawingTool drawingTool = AnnotationDrawingTool.FreeDraw;
    private AnnotationEditorGesture gesture;
    private AnnotationPoint? pointerStart;
    private AnnotationPoint? pointerCurrent;
    private AnnotationShape? operationShape;
    private IReadOnlyList<AnnotationShape>? operationSnapshot;
    private List<AnnotationPoint>? draftPath;
    private int? selectedIndex;
    private bool operationChanged;

    internal AnnotationDrawingTool DrawingTool
    {
        get => drawingTool;
        set
        {
            if (drawingTool == value)
            {
                return;
            }

            CancelPointer();
            drawingTool = value;
        }
    }

    internal IReadOnlyList<AnnotationShape> Shapes => shapes;

    internal int? SelectedIndex => selectedIndex;

    internal AnnotationShape? SelectedShape => selectedIndex.HasValue && selectedIndex.Value < shapes.Count
        ? shapes[selectedIndex.Value]
        : null;

    internal bool CanUndo => undoHistory.Count > 0;

    internal bool CanDelete => SelectedShape is not null;

    internal AnnotationShape? DraftShape
    {
        get
        {
            if (!pointerStart.HasValue || !pointerCurrent.HasValue)
            {
                return null;
            }

            return gesture switch
            {
                AnnotationEditorGesture.DrawRectangle => CreateBoundsShape(
                    AnnotationShapeKind.Rectangle,
                    pointerStart.Value,
                    pointerCurrent.Value),
                AnnotationEditorGesture.DrawEllipse => CreateBoundsShape(
                    AnnotationShapeKind.Ellipse,
                    pointerStart.Value,
                    pointerCurrent.Value),
                _ => null
            };
        }
    }

    internal IReadOnlyList<AnnotationPoint> DraftPath => draftPath is null
        ? Array.Empty<AnnotationPoint>()
        : draftPath;

    internal void PointerDown(AnnotationPoint point, double hitTolerance)
    {
        CancelPointer();
        if (drawingTool == AnnotationDrawingTool.Select)
        {
            BeginSelectionGesture(point, Math.Max(0, hitTolerance));
            return;
        }

        selectedIndex = null;
        operationSnapshot = Snapshot();
        pointerStart = point;
        pointerCurrent = point;
        gesture = drawingTool switch
        {
            AnnotationDrawingTool.Rectangle => AnnotationEditorGesture.DrawRectangle,
            AnnotationDrawingTool.Ellipse => AnnotationEditorGesture.DrawEllipse,
            AnnotationDrawingTool.FreeDraw => AnnotationEditorGesture.DrawFree,
            _ => AnnotationEditorGesture.None
        };
        if (gesture == AnnotationEditorGesture.DrawFree)
        {
            draftPath = [point];
        }
    }

    internal void PointerMoved(AnnotationPoint point)
    {
        pointerCurrent = point;
        switch (gesture)
        {
            case AnnotationEditorGesture.DrawFree:
                AppendPathPoint(point);
                break;
            case AnnotationEditorGesture.Move:
                UpdateMove(point);
                break;
            case AnnotationEditorGesture.Resize:
                UpdateResize(point);
                break;
        }
    }

    internal void PointerUp(AnnotationPoint point)
    {
        PointerMoved(point);
        switch (gesture)
        {
            case AnnotationEditorGesture.DrawRectangle:
            case AnnotationEditorGesture.DrawEllipse:
                CommitBoundsShape();
                break;
            case AnnotationEditorGesture.DrawFree:
                CommitFreeDraw();
                break;
            case AnnotationEditorGesture.Move:
            case AnnotationEditorGesture.Resize:
                CommitEdit();
                break;
        }

        ClearPointerState();
    }

    internal void CancelPointer()
    {
        if (operationSnapshot is not null && gesture is AnnotationEditorGesture.Move or AnnotationEditorGesture.Resize)
        {
            Restore(operationSnapshot);
        }

        ClearPointerState();
    }

    internal void DeleteSelected()
    {
        if (!selectedIndex.HasValue || selectedIndex.Value >= shapes.Count)
        {
            return;
        }

        undoHistory.Push(Snapshot());
        shapes.RemoveAt(selectedIndex.Value);
        selectedIndex = null;
        ClearPointerState();
    }

    internal void SetSelectedText(string? text)
    {
        if (!selectedIndex.HasValue || selectedIndex.Value >= shapes.Count)
        {
            return;
        }

        var shape = shapes[selectedIndex.Value];
        shapes[selectedIndex.Value] = shape with
        {
            Text = text ?? string.Empty
        };
    }

    internal void Undo()
    {
        if (!undoHistory.TryPop(out var snapshot))
        {
            return;
        }

        Restore(snapshot);
        selectedIndex = null;
        ClearPointerState();
    }

    private void BeginSelectionGesture(AnnotationPoint point, double hitTolerance)
    {
        var selectedShape = SelectedShape;
        if (selectedShape is not null && IsNearResizeHandle(selectedShape, point, hitTolerance))
        {
            BeginEdit(AnnotationEditorGesture.Resize, point, selectedShape);
            return;
        }

        selectedIndex = HitTest(point, hitTolerance);
        selectedShape = SelectedShape;
        if (selectedShape is not null)
        {
            BeginEdit(AnnotationEditorGesture.Move, point, selectedShape);
        }
    }

    private void BeginEdit(AnnotationEditorGesture editGesture, AnnotationPoint point, AnnotationShape shape)
    {
        gesture = editGesture;
        pointerStart = point;
        pointerCurrent = point;
        operationShape = shape;
        operationSnapshot = Snapshot();
    }

    private void CommitBoundsShape()
    {
        var draft = DraftShape;
        if (draft is null || draft.Width < MinimumShapeSize || draft.Height < MinimumShapeSize)
        {
            return;
        }

        PushOperationSnapshot();
        shapes.Add(draft);
        selectedIndex = shapes.Count - 1;
    }

    private void CommitFreeDraw()
    {
        if (draftPath is null || draftPath.Count < 2)
        {
            return;
        }

        var shape = new AnnotationShape(draftPath);
        if (shape.Width < MinimumShapeSize && shape.Height < MinimumShapeSize)
        {
            return;
        }

        PushOperationSnapshot();
        shapes.Add(shape);
        selectedIndex = shapes.Count - 1;
    }

    private void CommitEdit()
    {
        if (operationChanged)
        {
            PushOperationSnapshot();
        }
    }

    private void PushOperationSnapshot()
    {
        if (operationSnapshot is not null)
        {
            undoHistory.Push(operationSnapshot);
        }
    }

    private void AppendPathPoint(AnnotationPoint point)
    {
        if (draftPath is null)
        {
            return;
        }

        var previous = draftPath[^1];
        if (Distance(previous, point) >= MinimumPathPointDistance)
        {
            draftPath.Add(point);
        }
    }

    private void UpdateMove(AnnotationPoint point)
    {
        if (!selectedIndex.HasValue || operationShape is null || !pointerStart.HasValue)
        {
            return;
        }

        var deltaX = point.X - pointerStart.Value.X;
        var deltaY = point.Y - pointerStart.Value.Y;
        deltaX = Math.Clamp(deltaX, -operationShape.X, 1 - operationShape.X - operationShape.Width);
        deltaY = Math.Clamp(deltaY, -operationShape.Y, 1 - operationShape.Y - operationShape.Height);
        shapes[selectedIndex.Value] = OffsetShape(operationShape, deltaX, deltaY);
        operationChanged = Math.Abs(deltaX) > double.Epsilon || Math.Abs(deltaY) > double.Epsilon;
    }

    private void UpdateResize(AnnotationPoint point)
    {
        if (!selectedIndex.HasValue || operationShape is null)
        {
            return;
        }

        var width = Math.Clamp(point.X - operationShape.X, MinimumShapeSize, 1 - operationShape.X);
        var height = Math.Clamp(point.Y - operationShape.Y, MinimumShapeSize, 1 - operationShape.Y);
        shapes[selectedIndex.Value] = ResizeShape(operationShape, width, height);
        operationChanged = Math.Abs(width - operationShape.Width) > double.Epsilon
                           || Math.Abs(height - operationShape.Height) > double.Epsilon;
    }

    private int? HitTest(AnnotationPoint point, double tolerance)
    {
        for (var index = shapes.Count - 1; index >= 0; index--)
        {
            if (Contains(shapes[index], point, tolerance))
            {
                return index;
            }
        }

        return null;
    }

    private static bool Contains(AnnotationShape shape, AnnotationPoint point, double tolerance)
    {
        if (shape.Kind == AnnotationShapeKind.FreeDraw)
        {
            for (var index = 1; index < shape.Points.Count; index++)
            {
                if (DistanceToSegment(point, shape.Points[index - 1], shape.Points[index]) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        if (shape.Kind == AnnotationShapeKind.Ellipse)
        {
            var radiusX = Math.Max(shape.Width / 2, tolerance);
            var radiusY = Math.Max(shape.Height / 2, tolerance);
            var normalizedX = (point.X - shape.X - (shape.Width / 2)) / radiusX;
            var normalizedY = (point.Y - shape.Y - (shape.Height / 2)) / radiusY;
            return (normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1;
        }

        return point.X >= shape.X - tolerance
               && point.X <= shape.X + shape.Width + tolerance
               && point.Y >= shape.Y - tolerance
               && point.Y <= shape.Y + shape.Height + tolerance;
    }

    private static bool IsNearResizeHandle(AnnotationShape shape, AnnotationPoint point, double tolerance)
    {
        var handle = new AnnotationPoint(shape.X + shape.Width, shape.Y + shape.Height);
        return Distance(handle, point) <= Math.Max(tolerance * 1.5, MinimumShapeSize);
    }

    private static AnnotationShape CreateBoundsShape(
        AnnotationShapeKind kind,
        AnnotationPoint first,
        AnnotationPoint second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        return new AnnotationShape(
            kind,
            left,
            top,
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));
    }

    private static AnnotationShape OffsetShape(AnnotationShape shape, double deltaX, double deltaY)
    {
        if (shape.Kind == AnnotationShapeKind.FreeDraw)
        {
            return CopyStyle(
                new AnnotationShape(shape.Points.Select(point => new AnnotationPoint(point.X + deltaX, point.Y + deltaY))),
                shape);
        }

        return CopyStyle(new AnnotationShape(shape.Kind, shape.X + deltaX, shape.Y + deltaY, shape.Width, shape.Height), shape);
    }

    private static AnnotationShape ResizeShape(AnnotationShape shape, double width, double height)
    {
        if (shape.Kind == AnnotationShapeKind.FreeDraw)
        {
            var scaleX = shape.Width <= double.Epsilon ? 1 : width / shape.Width;
            var scaleY = shape.Height <= double.Epsilon ? 1 : height / shape.Height;
            return CopyStyle(
                new AnnotationShape(shape.Points.Select(point => new AnnotationPoint(
                    shape.X + ((point.X - shape.X) * scaleX),
                    shape.Y + ((point.Y - shape.Y) * scaleY)))),
                shape);
        }

        return CopyStyle(new AnnotationShape(shape.Kind, shape.X, shape.Y, width, height), shape);
    }

    private static AnnotationShape CopyStyle(AnnotationShape destination, AnnotationShape source)
    {
        return destination with
        {
            Text = source.Text,
            StrokeColor = source.StrokeColor,
            StrokeWidth = source.StrokeWidth
        };
    }

    private static double Distance(AnnotationPoint first, AnnotationPoint second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static double DistanceToSegment(AnnotationPoint point, AnnotationPoint start, AnnotationPoint end)
    {
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var lengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        if (lengthSquared <= double.Epsilon)
        {
            return Distance(point, start);
        }

        var projection = (((point.X - start.X) * segmentX) + ((point.Y - start.Y) * segmentY)) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);
        return Distance(point, new AnnotationPoint(start.X + (projection * segmentX), start.Y + (projection * segmentY)));
    }

    private IReadOnlyList<AnnotationShape> Snapshot() => shapes.ToArray();

    private void Restore(IReadOnlyList<AnnotationShape> snapshot)
    {
        shapes.Clear();
        shapes.AddRange(snapshot);
    }

    private void ClearPointerState()
    {
        gesture = AnnotationEditorGesture.None;
        pointerStart = null;
        pointerCurrent = null;
        operationShape = null;
        operationSnapshot = null;
        draftPath = null;
        operationChanged = false;
    }

    private enum AnnotationEditorGesture
    {
        None,
        DrawRectangle,
        DrawEllipse,
        DrawFree,
        Move,
        Resize
    }
}
