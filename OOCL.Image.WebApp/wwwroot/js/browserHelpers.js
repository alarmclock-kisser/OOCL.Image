// JS-Interop helper für Browser-Einstellungen
// Muss vor _framework/blazor.server.js geladen werden (siehe Pages/_Host.cshtml)

window.browserHelpers = window.browserHelpers || {};

window.browserHelpers.prefersDark = function () {
    try {
        if (window.matchMedia) {
            return window.matchMedia('(prefers-color-scheme: dark)').matches === true;
        }
        return false;
    } catch (e) {
        // Falls unerwarteter Fehler: safe default
        return false;
    }
};