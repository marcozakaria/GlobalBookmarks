using System.Collections.Generic;
using UnityEngine;

namespace GlobalBookmarks
{
    public class GlobalBookmarksData : ScriptableObject
    {
        [SerializeField] public List<Bookmark> bookmarks = new List<Bookmark>();
    }
}
