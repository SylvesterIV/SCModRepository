﻿namespace RichHudFramework.UI
{
    public class SelectionBoxEntry<TElement> : HudElementContainer<TElement>, ISelectionBoxEntry<TElement>
        where TElement : HudElementBase
    {
        public SelectionBoxEntry()
        {
            Enabled = true;
            AllowHighlighting = true;
        }

        public virtual bool Enabled { get; set; }

        public virtual bool AllowHighlighting { get; set; }

        public virtual void Reset()
        {
            Enabled = true;
            AllowHighlighting = true;
        }
    }

    public class SelectionBoxEntryTuple<TElement, TValue>
        : SelectionBoxEntry<TElement>, ISelectionBoxEntryTuple<TElement, TValue>
        where TElement : HudElementBase
    {
        public TValue AssocMember { get; set; }

        public override void Reset()
        {
            Enabled = true;
            AllowHighlighting = true;
            AssocMember = default(TValue);
        }
    }
}