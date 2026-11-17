mergeInto(LibraryManager.library, {
  // Unity's systemCopyBuffer never reaches the browser clipboard on WebGL.
  AG_CopyText: function (ptr) {
    var text = UTF8ToString(ptr);
    try {
      if (navigator.clipboard) navigator.clipboard.writeText(text).catch(function () {});
    } catch (e) {}
  }
});
