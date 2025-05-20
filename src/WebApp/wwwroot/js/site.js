// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

function showGraphAPI() {
    const triggerEl = document.querySelector('#helpSelector button[data-bs-target="#microsoftGraph"]')
    bootstrap.Tab.getInstance(triggerEl).show() // Select tab by name
}

function showPowerShell() {
    const triggerEl = document.querySelector('#helpSelector button[data-bs-target="#graphPowerShell"]')
    bootstrap.Tab.getInstance(triggerEl).show() // Select tab by name
}

// When the page is loaded start the demo
var waitForJQuery = setInterval(function () {
    if (typeof $ != 'undefined') {
        clearInterval(waitForJQuery);
        checkForHashParam(1);
    }
}, 500);

// Listen to the URL hash change event
window.addEventListener("hashchange", function () {
    checkForHashParam(3);
});

function checkForHashParam(eventType) {
    var myUrl = new URL(window.location.href.replace(/#/g, "?"));

    var graph = myUrl.searchParams.get("graph");
    var powerShell = myUrl.searchParams.get("ps");
    var usecase = myUrl.searchParams.get("usecase");
    var cmd = myUrl.searchParams.get("cmd");

    // If searchParams is empty, hide the offcanvas and exist
    if (myUrl.searchParams === null || myUrl.searchParams.size == 0 || (graph === null && powerShell === null && usecase === null && cmd === null)) {
        $("#offcanvasRight").offcanvas('hide');
        return;
    }

    if (graph != null) {
        // Show help's Graph
        showGraphAPI();
    }
    else if (powerShell != null) {
        // Show help's Graph
        showPowerShell();
    }
    else {
        // Start a demo
        showUseCase(eventType);
    }
}

// Show the demo
function showUseCase(trigger) {
    var myUrl = new URL(window.location.href.replace(/#/g, "?"));
    var usecase = myUrl.searchParams.get("usecase");
    var cmd = myUrl.searchParams.get("cmd");

    if (cmd === "StepUpCompleted") {
        completeOrder();
        return;
    }

    // Click on the right button
    if (trigger === 2) {
        usecase = 'Default';
    }

    var useCases = ["Default", "SignUpLink", "CloudflareInteractiveChallenge", "CloudflareJsChallenge", "CloudflareNetwork", "ArkoseFraudProtection", "MSA", "SPA", "NativeAuth", "EmailOtp", "OnlineRetail", "DisableAccount", "CustomDomain", "CustomEmail", "AssignmentRequired", "StepUp", "CSA", "PolicyAgreement", "EmailAndPassword", "OBO", "SSO", "GithubWorkflows", "TokenTTL", "MFA", "CA", "ForceSignIn", "UserInsights", "SignInLog", "ModifyAttributeValues", "BlockSignUp", "CompanyBranding", "Language", "PreSelectLanguage", "SSPR", "Social", "ActAs", "LoginHint", "TokenAugmentation", "TokenClaims", "PreAttributeCollection", "PostAttributeCollection", "ProfileEdit", "DeleteAccount", "UserLastActivity", "RBAC", "GBAC", "CustomAttributes", "Kiosk", "Saml"];

    if (($('#offcanvasRight').length > 0) && usecase && (useCases.indexOf(usecase) > -1)) {

        $("#offcanvasRight").offcanvas('show');
        useCaseId = "useCase_" + usecase;

        $(".useCase").hide();
        $("#" + useCaseId).show();

        // Telemetry to improve the demo 
        var triggerIDs = ["Link", "Start", "Select"];

        $.get("/SelectUseCase/usecase?id=" + useCaseId.replace('useCase_', '') + "&trigger=" + triggerIDs[trigger - 1] + "&referral=" + document.referrer, function (data) {

        }).fail(function (response) {
            console.log("Telemetry error:");
            console.log(response)
        });
    }
    else {
        $("#offcanvasRight").offcanvas('hide');
        window.location.hash = '';
    }
}

/********* Stepper *********/
var stepper
$(document).ready(function () {

    $('.feedback').popover({ placement: "top", trigger: "hover", content: "Found a bug or have a question? Want to provide feedback? Click on this button and raise an issue on GitHub." });

    if ($('.pop').length > 0) {
        $('.pop').on('click', function () {
            $('.imagepreview').attr('src', $(this).find('img').attr('src'));
            $('#imagemodal').modal('show');
        });
    }

    if ($('.bs-stepper').length > 0) {

        stepper = new Stepper($('.bs-stepper')[0], {
            linear: false,
            animation: true
        })

        // Add the links to the pages
        if ($('#stepNavigator').length > 0) {

            var items = $('.bs-stepper-pane').length;

            for (let i = 0; i < items; i++) {
                $('#stepNavigator').append('<li><a class="dropdown-item" onclick="stepper.to(' + (i + 1) + '); return false;" href="#">' + (i + 1) + '</a></li>')
            }
        }

        $('.bs-stepper')[0].addEventListener('shown.bs-stepper', function (event) {

            $("#stepNumber").html(event.detail.indexStep + 1)

            if (event.detail.indexStep == 0) {
                // Disable previous button
                $("#movePrevious").css("pointer-events", "none");
                $("#movePrevious").css("color", "gray");
            }
            else {
                // Enable previous button
                $("#movePrevious").css("pointer-events", "auto");
                $("#movePrevious").css("color", "");
            }

            if (event.detail.indexStep + 1 == $('.bs-stepper-pane').length) {
                // Disable next button
                $("#moveNext").css("pointer-events", "none");
                $("#moveNext").css("color", "gray");
            }
            else {
                // Enable steps  next button
                $("#moveNext").css("pointer-events", "auto");
                $("#moveNext").css("color", "");
            }

        })
    }
});
