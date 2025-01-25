using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace Application.Common.Models;

public  record UploadFile(
    string FileName,
    string ContentType,
    byte[] FileContent,
    string FileExtension,
    long FileSize);