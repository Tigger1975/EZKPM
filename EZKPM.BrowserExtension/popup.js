document.addEventListener('DOMContentLoaded', () => {
    const searchInput = document.getElementById('searchInput');
    const resultsContainer = document.getElementById('resultsContainer');
    const statusMsg = document.getElementById('statusMsg');
    
    // Initial Request (empty query, will return default)
    // Actually, let's just get the active tab URL and search for it first.
    chrome.tabs.query({active: true, currentWindow: true}, function(tabs) {
        let currentHost = "";
        if (tabs && tabs[0] && tabs[0].url) {
            try {
                const url = new URL(tabs[0].url);
                currentHost = url.hostname.replace('www.', '');
            } catch(e) {}
        }
        
        searchInput.value = currentHost;
        performSearch(currentHost);
    });

    let searchTimeout;
    searchInput.addEventListener('input', (e) => {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(() => {
            performSearch(e.target.value);
        }, 300);
    });

    function performSearch(query) {
        resultsContainer.innerHTML = '<div class="loading">Searching...</div>';
        
        chrome.runtime.sendMessage({ 
            type: "REQUEST_SEARCH", 
            query: query 
        });
    }

    chrome.runtime.onMessage.addListener((message) => {
        if (message.Type === "SEARCH_RESULTS") {
            renderResults(message.Results);
        } else if (message.Type === "CREDENTIAL_DATA_RESPONSE") {
            if (pendingAction === 'pass' && message.Password) {
                navigator.clipboard.writeText(message.Password);
                showStatus("Password copied!");
            } else if (pendingAction === 'totp') {
                if (message.TotpCode) {
                    navigator.clipboard.writeText(message.TotpCode);
                    showStatus("TOTP copied!");
                } else {
                    showStatus("No TOTP available", true);
                }
            } else if (pendingAction === 'fill') {
                chrome.tabs.query({active: true, currentWindow: true}, function(tabs) {
                    if (tabs && tabs[0]) {
                        chrome.tabs.sendMessage(tabs[0].id, {
                            Type: "CREDENTIAL_DATA_RESPONSE",
                            Password: message.Password,
                            TotpCode: message.TotpCode,
                            CustomFields: message.CustomFields
                        });
                        showStatus("Injected!");
                    }
                });
            }
            pendingAction = null;
        } else if (message.Type === "AUDIT_REJECTED" && pendingAction) {
            showStatus("Rejected by Desktop!", true);
            pendingAction = null;
        }
    });

    let pendingAction = null;

    function renderResults(results) {
        resultsContainer.innerHTML = '';
        if (!results || results.length === 0) {
            resultsContainer.innerHTML = '<div class="loading">No credentials found.</div>';
            return;
        }

        results.forEach(asset => {
            const item = document.createElement('div');
            item.className = 'asset-item';
            
            let icon = '🔑';
            if (asset.AssetType === 'Payment') icon = '💳';
            else if (asset.AssetType === 'SecureNote') icon = '📝';

            item.innerHTML = `
                <div class="asset-title">${icon} ${asset.Title || 'Untitled'}</div>
                <div class="asset-username">${asset.Username || asset.Url || 'No details'}</div>
                <div class="action-buttons">
                    <button class="btn autofill" data-id="${asset.AssetId}">Fill</button>
                    <button class="btn copy-user" data-user="${asset.Username}">User</button>
                    <button class="btn copy-pass" data-id="${asset.AssetId}">Pass</button>
                    <button class="btn copy-totp" data-id="${asset.AssetId}">TOTP</button>
                </div>
            `;
            
            resultsContainer.appendChild(item);
        });

        // Event Listeners for Buttons
        resultsContainer.querySelectorAll('.autofill').forEach(btn => {
            btn.addEventListener('click', (e) => {
                pendingAction = 'fill';
                const id = e.target.getAttribute('data-id');
                chrome.runtime.sendMessage({ type: "REQUEST_CREDENTIAL_DATA", assetId: id });
            });
        });

        resultsContainer.querySelectorAll('.copy-user').forEach(btn => {
            btn.addEventListener('click', async (e) => {
                const user = e.target.getAttribute('data-user');
                if (user && user !== 'null') {
                    await navigator.clipboard.writeText(user);
                    showStatus("Username copied!");
                }
            });
        });

        resultsContainer.querySelectorAll('.copy-pass').forEach(btn => {
            btn.addEventListener('click', (e) => {
                pendingAction = 'pass';
                const id = e.target.getAttribute('data-id');
                chrome.runtime.sendMessage({ type: "REQUEST_CREDENTIAL_DATA", assetId: id });
            });
        });

        resultsContainer.querySelectorAll('.copy-totp').forEach(btn => {
            btn.addEventListener('click', (e) => {
                pendingAction = 'totp';
                const id = e.target.getAttribute('data-id');
                chrome.runtime.sendMessage({ type: "REQUEST_CREDENTIAL_DATA", assetId: id });
            });
        });
    }

    function showStatus(msg, isError = false) {
        statusMsg.innerText = msg;
        statusMsg.style.backgroundColor = isError ? '#f44336' : '#4CAF50';
        statusMsg.style.opacity = '1';
        setTimeout(() => {
            statusMsg.style.opacity = '0';
        }, 2000);
    }
});
