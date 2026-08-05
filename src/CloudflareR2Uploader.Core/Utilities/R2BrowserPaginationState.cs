using System;
using System.Collections.Generic;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Stores opaque ListObjectsV2 tokens per prefix so Previous never guesses a token.
    /// </summary>
    public sealed class R2BrowserPaginationState
    {
        private readonly List<string> _pageTokens = new List<string>();

        public R2BrowserPaginationState()
        {
            Reset(string.Empty);
        }

        public string Prefix { get; private set; }
        public int PageIndex { get; private set; }
        public string NextContinuationToken { get; private set; }
        public IList<string> PageTokens { get { return _pageTokens.AsReadOnly(); } }
        public string CurrentToken { get { return _pageTokens[PageIndex]; } }
        public bool CanMovePrevious { get { return PageIndex > 0; } }
        public bool CanMoveNext { get { return !string.IsNullOrEmpty(NextContinuationToken); } }

        public void Reset(string prefix)
        {
            Prefix = R2BrowserPathUtility.NormalizePrefix(prefix);
            _pageTokens.Clear();
            _pageTokens.Add(null);
            PageIndex = 0;
            NextContinuationToken = null;
        }

        public void SetNextContinuationToken(string token)
        {
            NextContinuationToken = token;
        }

        public bool MoveNext()
        {
            if (string.IsNullOrEmpty(NextContinuationToken)) return false;

            int nextIndex = PageIndex + 1;
            if (_pageTokens.Count > nextIndex)
            {
                if (!string.Equals(_pageTokens[nextIndex], NextContinuationToken, StringComparison.Ordinal))
                {
                    _pageTokens.RemoveRange(nextIndex, _pageTokens.Count - nextIndex);
                    _pageTokens.Add(NextContinuationToken);
                }
            }
            else
            {
                _pageTokens.Add(NextContinuationToken);
            }

            PageIndex = nextIndex;
            NextContinuationToken = null;
            return true;
        }

        public bool MovePrevious()
        {
            if (PageIndex <= 0) return false;
            PageIndex--;
            NextContinuationToken = null;
            return true;
        }
    }
}
