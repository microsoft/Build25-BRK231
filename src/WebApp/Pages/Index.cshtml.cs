// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAppConRazor.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ILogger<IndexModel> logger)
    {
        _logger = logger;
    }

    public void OnGet()
    {

    }
}
