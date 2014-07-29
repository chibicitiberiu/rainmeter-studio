﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RainmeterStudio.Documents.DocumentEditorFeatures
{
    public interface ICustomDocumentTitleProvider
    {
        string Title { get; }
        event EventHandler TitleChanged;
    }
}
