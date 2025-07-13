// Global state
let currentApiKey = '';
let currentTenantInfo = null;
let files = [];
let subtenants = {};
let currentPath = []; // Array of tenant IDs representing current navigation path
let tenantCache = {}; // Cache for tenant information

// Custom modal functions
function showAlert(message, title = 'Alert', type = 'info') {
    return new Promise((resolve) => {
        const modal = document.getElementById('alert-modal');
        const icon = document.getElementById('alert-icon');
        const titleElement = document.getElementById('alert-title');
        const messageElement = document.getElementById('alert-message');
        const okBtn = document.getElementById('alert-ok-btn');
        
        // Set content
        titleElement.textContent = title;
        messageElement.textContent = message;
        
        // Set icon based on type
        icon.className = `alert-icon ${type}`;
        switch (type) {
            case 'success':
                icon.textContent = '✓';
                break;
            case 'error':
                icon.textContent = '✕';
                break;
            case 'warning':
                icon.textContent = '⚠';
                break;
            default:
                icon.textContent = 'ℹ';
                break;
        }
        
        // Show modal
        modal.style.display = 'flex';
        
        // Handle OK button
        const handleOk = () => {
            modal.style.display = 'none';
            okBtn.removeEventListener('click', handleOk);
            resolve();
        };
        
        okBtn.addEventListener('click', handleOk);
        
        // Handle clicking outside modal
        const handleOutsideClick = (e) => {
            if (e.target === modal) {
                handleOk();
            }
        };
        
        modal.addEventListener('click', handleOutsideClick);
    });
}

function showConfirm(message, title = 'Confirm') {
    return new Promise((resolve) => {
        const modal = document.getElementById('confirm-modal');
        const titleElement = document.getElementById('confirm-title');
        const messageElement = document.getElementById('confirm-message');
        const okBtn = document.getElementById('confirm-ok-btn');
        const cancelBtn = document.getElementById('confirm-cancel-btn');
        
        // Set content
        titleElement.textContent = title;
        messageElement.textContent = message;
        
        // Show modal
        modal.style.display = 'flex';
        
        // Handle buttons
        const handleOk = () => {
            modal.style.display = 'none';
            okBtn.removeEventListener('click', handleOk);
            cancelBtn.removeEventListener('click', handleCancel);
            modal.removeEventListener('click', handleOutsideClick);
            resolve(true);
        };
        
        const handleCancel = () => {
            modal.style.display = 'none';
            okBtn.removeEventListener('click', handleOk);
            cancelBtn.removeEventListener('click', handleCancel);
            modal.removeEventListener('click', handleOutsideClick);
            resolve(false);
        };
        
        const handleOutsideClick = (e) => {
            if (e.target === modal) {
                handleCancel();
            }
        };
        
        okBtn.addEventListener('click', handleOk);
        cancelBtn.addEventListener('click', handleCancel);
        modal.addEventListener('click', handleOutsideClick);
    });
}

function showPrompt(message, title = 'Prompt', defaultValue = '') {
    return new Promise((resolve) => {
        const modal = document.getElementById('prompt-modal');
        const titleElement = document.getElementById('prompt-title');
        const messageElement = document.querySelector('.prompt-message label');
        const inputElement = document.getElementById('folder-name-input');
        const okBtn = document.getElementById('prompt-ok-btn');
        const cancelBtn = document.getElementById('prompt-cancel-btn');
        
        // Set content
        titleElement.textContent = title;
        messageElement.textContent = message;
        inputElement.value = defaultValue;
        
        // Show modal and focus input
        modal.style.display = 'flex';
        inputElement.focus();
        inputElement.select();
        
        // Handle buttons
        const handleOk = () => {
            const value = inputElement.value.trim();
            modal.style.display = 'none';
            okBtn.removeEventListener('click', handleOk);
            cancelBtn.removeEventListener('click', handleCancel);
            inputElement.removeEventListener('keypress', handleKeyPress);
            modal.removeEventListener('click', handleOutsideClick);
            resolve(value);
        };
        
        const handleCancel = () => {
            modal.style.display = 'none';
            okBtn.removeEventListener('click', handleOk);
            cancelBtn.removeEventListener('click', handleCancel);
            inputElement.removeEventListener('keypress', handleKeyPress);
            modal.removeEventListener('click', handleOutsideClick);
            resolve(null);
        };
        
        const handleKeyPress = (e) => {
            if (e.key === 'Enter') {
                handleOk();
            } else if (e.key === 'Escape') {
                handleCancel();
            }
        };
        
        const handleOutsideClick = (e) => {
            if (e.target === modal) {
                handleCancel();
            }
        };
        
        okBtn.addEventListener('click', handleOk);
        cancelBtn.addEventListener('click', handleCancel);
        inputElement.addEventListener('keypress', handleKeyPress);
        modal.addEventListener('click', handleOutsideClick);
    });
}

// API endpoints
const API_BASE = (() => {
    // Get the current path and find the base path
    const pathname = window.location.pathname;
    
    // If we're at the root, just use origin
    if (pathname === '/' || pathname === '') {
        return window.location.origin;
    }
    
    // If we're in a subdirectory (like /byte-shelf/), use that as the base
    // Remove trailing slash if present
    const basePath = pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
    return window.location.origin + basePath;
})();

// Utility functions
function formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function formatDate(dateString) {
    return new Date(dateString).toLocaleString();
}

// API functions
async function makeApiRequest(endpoint, options = {}) {
    const url = `${API_BASE}${endpoint}`;
    const config = {
        headers: {
            'X-API-Key': currentApiKey,
            'Content-Type': 'application/json',
            ...options.headers
        },
        ...options
    };

    try {
        const response = await fetch(url, config);
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        return await response.json();
    } catch (error) {
        console.error('API request failed:', error);
        throw error;
    }
}

async function makeApiRequestWithBody(endpoint, method = 'GET', body = null) {
    const options = {
        method,
        headers: {
            'X-API-Key': currentApiKey,
            'Content-Type': 'application/json'
        }
    };

    if (body) {
        options.body = JSON.stringify(body);
    }

    return makeApiRequest(endpoint, options);
}

// Authentication
async function authenticate() {
    const apiKey = document.getElementById('api-key').value.trim();
    if (!apiKey) {
        showError('Please enter an API key');
        return;
    }

    try {
        currentApiKey = apiKey;
        
        // Test authentication by getting tenant info
        const tenantInfo = await makeApiRequest('/api/tenant/info');
        currentTenantInfo = tenantInfo;
        
        // Show main application
        document.getElementById('auth-section').style.display = 'none';
        document.getElementById('main-section').style.display = 'block';
        
        // Load initial data
        await loadTenantInfo();
        await loadFiles();
        
        // Show admin button if user is admin
        if (currentTenantInfo.isAdmin) {
            document.getElementById('admin-btn').style.display = 'inline-block';
        }
        
        // Setup event listeners
        setupEventListeners();
        
    } catch (error) {
        showError('Authentication failed: ' + error.message);
        currentApiKey = '';
    }
}

function logout() {
    currentApiKey = '';
    currentTenantInfo = null;
    files = [];
    
    document.getElementById('auth-section').style.display = 'flex';
    document.getElementById('main-section').style.display = 'none';
    document.getElementById('api-key').value = '';
    document.getElementById('auth-error').style.display = 'none';
    
    // Hide admin button when logging out
    document.getElementById('admin-btn').style.display = 'none';
}

function showError(message) {
    const errorElement = document.getElementById('auth-error');
    errorElement.textContent = message;
    errorElement.style.display = 'block';
}

// Tenant information
async function loadTenantInfo() {
    try {
        const tenantInfo = await makeApiRequest('/api/tenant/info');
        
        document.getElementById('tenant-name').textContent = tenantInfo.displayName;
        document.getElementById('used-storage').textContent = formatBytes(tenantInfo.currentUsageBytes);
        document.getElementById('available-storage').textContent = formatBytes(tenantInfo.availableSpaceBytes);
        document.getElementById('total-storage').textContent = formatBytes(tenantInfo.storageLimitBytes);
        document.getElementById('usage-percentage').textContent = `${tenantInfo.usagePercentage.toFixed(1)}%`;
        
        const usageBar = document.getElementById('usage-bar');
        usageBar.style.width = `${Math.min(tenantInfo.usagePercentage, 100)}%`;
        
        if (tenantInfo.usagePercentage > 90) {
            usageBar.style.background = 'linear-gradient(90deg, #dc3545, #c82333)';
        } else if (tenantInfo.usagePercentage > 75) {
            usageBar.style.background = 'linear-gradient(90deg, #ffc107, #e0a800)';
        }
        
    } catch (error) {
        console.error('Failed to load tenant info:', error);
    }
}

// File management
async function loadFiles() {
    try {
        const filesList = document.getElementById('files-list');
        filesList.innerHTML = '<div class="loading">Loading files...</div>';
        
        // Load files for current tenant
        const targetTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
        const endpoint = targetTenantId ? `/api/files/${targetTenantId}` : '/api/files';
        files = await makeApiRequest(endpoint);
        
        // Load subtenants for current tenant
        await loadSubtenants();
        
        // Always use displayFilesAndFolders to ensure breadcrumb is shown when in subtenants
        displayFilesAndFolders(files, subtenants);
        
    } catch (error) {
        console.error('Failed to load files:', error);
        document.getElementById('files-list').innerHTML = '<div class="loading">Failed to load files</div>';
    }
}

async function loadSubtenants() {
    try {
        subtenants = {}; // Clear previous subtenants to avoid stale data
        const targetTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
        if (targetTenantId) {
            // We're in a subtenant, so we need to get its subtenants
            const result = await makeApiRequest(`/api/tenant/subtenants/${targetTenantId}/subtenants`);
            subtenants = result || {};
            // Also fetch and cache the display name of the current subtenant for breadcrumb
            if (!tenantCache[targetTenantId]) {
                const info = await makeApiRequest(`/api/tenant/subtenants/${targetTenantId}`);
                if (info && info.displayName) {
                    tenantCache[targetTenantId] = info;
                }
            }
        } else {
            // We're at the root, get subtenants of current tenant
            const result = await makeApiRequest('/api/tenant/subtenants');
            subtenants = result || {};
        }
    } catch (error) {
        console.error('Failed to load subtenants:', error);
        subtenants = {};
    }
}

function displayFilesAndFolders(filesToDisplay, subtenantsToDisplay) {
    const filesList = document.getElementById('files-list');
    
    // Create breadcrumb navigation
    const breadcrumb = createBreadcrumb();
    
    // Combine files and folders
    const items = [];
    
    // Add folders (subtenants) first
    Object.entries(subtenantsToDisplay).forEach(([tenantId, tenantInfo]) => {
        items.push({
            type: 'folder',
            id: tenantId,
            name: tenantInfo.displayName,
            tenantInfo: tenantInfo
        });
    });
    
    // Add files
    filesToDisplay.forEach(file => {
        items.push({
            type: 'file',
            id: file.id,
            name: file.originalFilename,
            file: file
        });
    });
    
    // Always display breadcrumb if we're in a subtenant, even if no items
    if (items.length === 0) {
        filesList.innerHTML = breadcrumb + '<div class="loading">No files or folders found</div>';
        return;
    }
    
    // Display breadcrumb and items
    filesList.innerHTML = breadcrumb + items.map(item => {
        if (item.type === 'folder') {
            return `
                <div class="file-item folder-item" onclick="navigateToFolder('${item.id}').catch(console.error)">
                    <div class="file-info">
                        <div class="file-name">
                            <span class="folder-icon">📁</span>
                            ${item.name}
                        </div>
                        <div class="file-meta">
                            ${formatBytes(item.tenantInfo.currentUsageBytes)} used
                        </div>
                    </div>
                    <div class="file-actions">
                        <button class="folder-btn" onclick="event.stopPropagation(); showFolderActions('${item.id}', '${item.name}')">⋯</button>
                    </div>
                </div>
            `;
        } else {
            return `
                <div class="file-item">
                    <div class="file-info">
                        <div class="file-name">${item.name}</div>
                        <div class="file-meta">
                            ${formatBytes(item.file.fileSize)} • ${formatDate(item.file.createdAt)} • ${item.file.chunkIds ? item.file.chunkIds.length : 0} chunks
                        </div>
                    </div>
                    <div class="file-actions">
                        <button class="download-btn" onclick="downloadFile('${item.id}')">Download</button>
                        <button class="delete-btn" onclick="deleteFile('${item.id}')">Delete</button>
                    </div>
                </div>
            `;
        }
    }).join('');
}

function createBreadcrumb() {
    if (currentPath.length === 0) {
        return '';
    }
    const breadcrumbItems = [
        { id: null, name: currentTenantInfo.displayName }
    ];
    for (let i = 0; i < currentPath.length; i++) {
        const tenantId = currentPath[i];
        let displayName = tenantId; // fallback to tenant ID
        if (tenantCache[tenantId]) {
            displayName = tenantCache[tenantId].displayName;
        } else if (i === currentPath.length - 1 && subtenants[tenantId]) {
            displayName = subtenants[tenantId].displayName;
        }
        breadcrumbItems.push({ id: tenantId, name: displayName });
    }
    return `
        <div class="breadcrumb">
            ${breadcrumbItems.map((item, index) => {
                if (index === breadcrumbItems.length - 1) {
                    return `<span class="breadcrumb-current">${item.name}</span>`;
                } else {
                    return `<span class="breadcrumb-item" onclick="navigateToPath(${index}).catch(console.error)">${item.name}</span>`;
                }
            }).join(' › ')}
        </div>
    `;
}

async function navigateToFolder(tenantId) {
    // Cache the current tenant info before navigating
    if (currentPath.length > 0) {
        const currentTenantId = currentPath[currentPath.length - 1];
        if (subtenants[currentTenantId]) {
            tenantCache[currentTenantId] = subtenants[currentTenantId];
        }
    }
    // Also cache the folder we're about to enter if we have its info
    if (subtenants[tenantId]) {
        tenantCache[tenantId] = subtenants[tenantId];
    }
    currentPath.push(tenantId);
    await loadFiles();
}

async function navigateToPath(index) {
    currentPath = currentPath.slice(0, index);
    await loadFiles();
}

async function createFolder(event) {
    if (event) {
        event.preventDefault();
    }
    
    // Show custom prompt for folder name
    const folderName = await showPrompt('Folder name:', 'Create Folder');
    if (!folderName || folderName.trim() === '') {
        return;
    }
    
    try {
        // Get the current tenant ID (where we're creating the subtenant)
        const currentTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
        
        let response;
        if (currentTenantId) {
            // We're inside a subtenant, create under the current subtenant
            response = await makeApiRequestWithBody(`/api/tenant/subtenants/${currentTenantId}/subtenants`, 'POST', {
                displayName: folderName.trim()
            });
        } else {
            // We're at root level, create under the authenticated tenant
            response = await makeApiRequestWithBody('/api/tenant/subtenants', 'POST', {
                displayName: folderName.trim()
            });
        }
        
        // Refresh the file list to show the new folder
        await loadFiles();
        
        await showAlert(`Folder "${folderName}" created successfully!`, 'Folder Created', 'success');
        
    } catch (error) {
        console.error('Failed to create folder:', error);
        await showAlert('Failed to create folder: ' + error.message, 'Create Failed', 'error');
    }
}

async function showFolderActions(tenantId, folderName) {
    // Show a custom modal with folder info and delete button
    const modal = document.getElementById('alert-modal');
    const title = document.getElementById('alert-title');
    const message = document.getElementById('alert-message');
    const icon = document.getElementById('alert-icon');
    const okBtn = document.getElementById('alert-ok-btn');
    const cancelBtn = document.getElementById('alert-cancel-btn');
    
    title.textContent = 'Folder Info';
    message.innerHTML = `
        <div style="text-align: left; margin-bottom: 16px;">
            <strong>Folder Name:</strong> ${folderName}<br>
            <strong>Tenant ID:</strong> ${tenantId}
        </div>
        <div style="text-align: center;">
            <button onclick="deleteSubtenant('${tenantId}', '${folderName}')" class="btn btn-danger" style="margin-right: 8px;">
                Delete Folder
            </button>
            <button onclick="closeFolderModal()" class="btn btn-secondary">
                Close
            </button>
        </div>
    `;
    icon.textContent = '📁';
    icon.className = 'alert-icon info';
    
    // Hide the default OK button since we have custom buttons
    okBtn.style.display = 'none';
    cancelBtn.style.display = 'none';
    
    modal.style.display = 'flex';
}

function closeFolderModal() {
    const modal = document.getElementById('alert-modal');
    const okBtn = document.getElementById('alert-ok-btn');
    const cancelBtn = document.getElementById('alert-cancel-btn');
    
    modal.style.display = 'none';
    // Restore the default OK button for future use
    okBtn.style.display = 'inline-block';
    cancelBtn.style.display = 'none';
}

async function deleteSubtenant(tenantId, folderName) {
    try {
        // Show confirmation dialog
        const confirmed = await showConfirm(
            `Are you sure you want to delete the folder "${folderName}"?\n\nThis will permanently delete the folder and all its contents. This action cannot be undone.`,
            'Delete Folder'
        );
        
        if (!confirmed) {
            return;
        }
        
        // Delete the subtenant using fetch directly (like deleteFile does)
        const deleteUrl = `${API_BASE}/api/tenant/subtenants/${tenantId}`;
        const response = await fetch(deleteUrl, {
            method: 'DELETE',
            headers: {
                'X-API-Key': currentApiKey
            }
        });
        
        if (!response.ok) {
            throw new Error(`Delete failed: ${response.statusText}`);
        }
        
        // Close the modal
        closeFolderModal();
        
        // Refresh the file list to show the updated folder list
        await loadFiles();
        
        // Refresh tenant info to update storage usage display
        await loadTenantInfo();
        
        await showAlert(`Folder "${folderName}" has been deleted successfully.`, 'Folder Deleted', 'success');
        
    } catch (error) {
        console.error('Failed to delete folder:', error);
        await showAlert('Failed to delete folder: ' + error.message, 'Delete Failed', 'error');
    }
}

function displayFiles(filesToDisplay) {
    const filesList = document.getElementById('files-list');
    
    if (filesToDisplay.length === 0) {
        filesList.innerHTML = '<div class="loading">No files found</div>';
        return;
    }
    
    filesList.innerHTML = filesToDisplay.map(file => `
        <div class="file-item">
            <div class="file-info">
                <div class="file-name">${file.originalFilename}</div>
                <div class="file-meta">
                    ${formatBytes(file.fileSize)} • ${formatDate(file.createdAt)} • ${file.chunkIds ? file.chunkIds.length : 0} chunks
                </div>
            </div>
            <div class="file-actions">
                <button class="download-btn" onclick="downloadFile('${file.id}')">Download</button>
                <button class="delete-btn" onclick="deleteFile('${file.id}')">Delete</button>
            </div>
        </div>
    `).join('');
}

async function refreshFiles(event) {
    if (event) {
        event.preventDefault();
    }
    await loadFiles();
}

// File upload
function setupEventListeners() {
    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('file-input');
    const searchInput = document.getElementById('search-input');
    
    // File input change
    fileInput.addEventListener('change', handleFileSelect);
    
    // Drag and drop
    dropZone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropZone.classList.add('dragover');
    });
    
    dropZone.addEventListener('dragleave', () => {
        dropZone.classList.remove('dragover');
    });
    
    dropZone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropZone.classList.remove('dragover');
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            handleFileUpload(files[0]);
        }
    });
    
    // Search functionality
    searchInput.addEventListener('input', (e) => {
        const searchTerm = e.target.value.toLowerCase();
        const filteredFiles = files.filter(file => 
            file.originalFilename.toLowerCase().includes(searchTerm)
        );
        displayFiles(filteredFiles);
    });
}

function handleFileSelect(event) {
    const file = event.target.files[0];
    if (file) {
        handleFileUpload(file);
    }
}

async function handleFileUpload(file) {
    try {
        // Check if we can store the file
        const uploadTargetTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
        const canStoreEndpoint = uploadTargetTenantId 
            ? `/api/tenant/storage/can-store?fileSizeBytes=${file.size}&targetTenantId=${uploadTargetTenantId}`
            : `/api/tenant/storage/can-store?fileSizeBytes=${file.size}`;
        const canStore = await makeApiRequest(canStoreEndpoint);
        
        if (!canStore.canStore) {
            await showAlert(`Cannot upload file: ${canStore.reason || 'Storage quota exceeded'}`, 'Upload Failed', 'error');
            return;
        }
        
        // Show upload progress
        const uploadProgress = document.getElementById('upload-progress');
        const uploadBar = document.getElementById('upload-bar');
        uploadProgress.style.display = 'block';
        uploadBar.style.width = '0%';
        
        // Create file metadata
        const fileId = crypto.randomUUID();
        const metadata = {
            id: fileId,
            originalFilename: file.name,
            contentType: file.type || 'application/octet-stream',
            fileSize: file.size,
            chunkIds: [] // Will be populated as chunks are uploaded
        };
        
        // Upload chunks
        const chunkSize = 1024 * 1024; // 1MB chunks
        const totalChunks = Math.ceil(file.size / chunkSize);
        const chunkIds = [];
        
        for (let i = 0; i < totalChunks; i++) {
            const start = i * chunkSize;
            const end = Math.min(start + chunkSize, file.size);
            const chunk = file.slice(start, end);
            const chunkId = crypto.randomUUID();
            
            // Upload chunk and get the returned chunk ID
            const uploadedChunkId = await uploadChunk(chunkId, chunk);
            chunkIds.push(uploadedChunkId);
            
            // Update progress
            const progress = ((i + 1) / totalChunks) * 100;
            uploadBar.style.width = `${progress}%`;
        }
        
        // Update metadata with the actual chunk IDs
        metadata.chunkIds = chunkIds;
        
        // Save file metadata
        const metadataTargetTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
        const metadataEndpoint = metadataTargetTenantId 
            ? `/api/files/${metadataTargetTenantId}/metadata`
            : '/api/files/metadata';
        await makeApiRequestWithBody(metadataEndpoint, 'POST', metadata);
        
        // Hide progress and refresh files
        uploadProgress.style.display = 'none';
        await loadFiles();
        await loadTenantInfo();
        
        await showAlert('File uploaded successfully!', 'Upload Complete', 'success');
        
    } catch (error) {
        console.error('Upload failed:', error);
        await showAlert('Upload failed: ' + error.message, 'Upload Failed', 'error');
        document.getElementById('upload-progress').style.display = 'none';
    }
}

async function uploadChunk(chunkId, chunk) {
    // Get the target tenant ID from current path
    const targetTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
    
    const chunkUrl = targetTenantId 
        ? `${API_BASE}/api/chunks/${targetTenantId}/${chunkId}`
        : `${API_BASE}/api/chunks/${chunkId}`;
        
    const response = await fetch(chunkUrl, {
        method: 'PUT',
        headers: {
            'X-API-Key': currentApiKey
        },
        body: chunk
    });
    
    if (!response.ok) {
        throw new Error(`Failed to upload chunk: ${response.statusText}`);
    }
    
    // Parse the response to get the actual chunk ID that was saved
    const result = await response.json();
    return result.ChunkId || chunkId; // Use returned ID or fallback to original
}

// File operations
async function downloadFile(fileId) {
    try {
        // Get the target tenant ID from current path
        const targetTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
        
        // Create a download link for the file
        const downloadUrl = targetTenantId 
            ? `${API_BASE}/api/files/${targetTenantId}/${fileId}/download`
            : `${API_BASE}/api/files/${fileId}/download`;
        
        // Create a temporary link element and trigger the download
        const link = document.createElement('a');
        link.href = downloadUrl;
        link.download = ''; // Let the browser determine the filename from the response headers
        link.style.display = 'none';
        
        // Add the API key as a header (we'll need to use fetch instead)
        const response = await fetch(downloadUrl, {
            headers: {
                'X-API-Key': currentApiKey
            }
        });
        
        if (!response.ok) {
            throw new Error(`Download failed: ${response.statusText}`);
        }
        
        // Get the filename from the Content-Disposition header or use a default
        const contentDisposition = response.headers.get('Content-Disposition');
        let filename = 'download';
        if (contentDisposition) {
            // Try multiple patterns to extract filename
            let filenameMatch = contentDisposition.match(/filename\*?=UTF-8''([^;]+)/);
            if (filenameMatch) {
                // Handle UTF-8 encoded filename
                filename = decodeURIComponent(filenameMatch[1]);
            } else {
                // Try standard filename format
                filenameMatch = contentDisposition.match(/filename="([^"]+)"/);
                if (filenameMatch) {
                    filename = filenameMatch[1];
                } else {
                    // Try filename without quotes
                    filenameMatch = contentDisposition.match(/filename=([^;]+)/);
                    if (filenameMatch) {
                        filename = filenameMatch[1];
                    }
                }
            }
        }
        
        // Create a blob and download it
        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        link.href = url;
        link.download = filename;
        
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        
        // Clean up the blob URL
        window.URL.revokeObjectURL(url);
        
    } catch (error) {
        console.error('Failed to download file:', error);
        await showAlert('Failed to download file: ' + error.message, 'Download Failed', 'error');
    }
}

async function deleteFile(fileId) {
    const confirmed = await showConfirm('Are you sure you want to delete this file?', 'Delete File');
    if (!confirmed) {
        return;
    }
    
    try {
        // Get the target tenant ID from current path
        const targetTenantId = currentPath.length > 0 ? currentPath[currentPath.length - 1] : null;
        
        // Use fetch directly since DELETE with NoContent() response can't be parsed as JSON
        const deleteUrl = targetTenantId 
            ? `${API_BASE}/api/files/${targetTenantId}/${fileId}`
            : `${API_BASE}/api/files/${fileId}`;
            
        const response = await fetch(deleteUrl, {
            method: 'DELETE',
            headers: {
                'X-API-Key': currentApiKey
            }
        });
        
        if (!response.ok) {
            throw new Error(`Delete failed: ${response.statusText}`);
        }
        
        await loadFiles();
        await loadTenantInfo();
        await showAlert('File deleted successfully!', 'Delete Complete', 'success');
        
    } catch (error) {
        console.error('Failed to delete file:', error);
        await showAlert('Failed to delete file: ' + error.message, 'Delete Failed', 'error');
    }
}

function showFileDetails(metadata) {
    const modal = document.getElementById('file-modal');
    const modalTitle = document.getElementById('modal-title');
    const modalContent = document.getElementById('modal-content');
    
    modalTitle.textContent = metadata.fileName;
    modalContent.innerHTML = `
        <div style="margin-bottom: 16px;">
            <strong>File Name:</strong> ${metadata.originalFilename}<br>
            <strong>Size:</strong> ${formatBytes(metadata.fileSize)}<br>
            <strong>Type:</strong> ${metadata.contentType}<br>
            <strong>Created:</strong> ${formatDate(metadata.createdAt)}<br>
            <strong>Chunks:</strong> ${metadata.chunkIds ? metadata.chunkIds.length : 0}
        </div>
        <div style="text-align: center;">
            <p style="color: #666; font-style: italic;">
                Full file download functionality would require implementing chunk reconstruction.
                This is a simplified demo showing file metadata.
            </p>
        </div>
    `;
    
    modal.style.display = 'flex';
}

function closeModal() {
    document.getElementById('file-modal').style.display = 'none';
}

// Admin Panel Functions
function showAdminPanel() {
    document.getElementById('admin-modal').style.display = 'flex';
    loadTenants();
}

function closeAdminModal() {
    document.getElementById('admin-modal').style.display = 'none';
}

function switchTab(tabName) {
    // Hide all tab contents
    document.querySelectorAll('.tab-content').forEach(tab => {
        tab.classList.remove('active');
    });
    
    // Remove active class from all tab buttons
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.classList.remove('active');
    });
    
    // Show selected tab content
    document.getElementById(`${tabName}-tab`).classList.add('active');
    
    // Add active class to clicked button
    event.target.classList.add('active');
}

async function loadTenants() {
    try {
        const tenantsList = document.getElementById('tenants-list');
        tenantsList.innerHTML = '<div class="loading">Loading tenants...</div>';
        
        const tenants = await makeApiRequest('/api/admin/tenants');
        
        if (tenants.length === 0) {
            tenantsList.innerHTML = '<div class="loading">No tenants found</div>';
            return;
        }
        
        tenantsList.innerHTML = tenants.map(tenant => `
            <div class="tenant-item">
                <div class="tenant-header">
                    <div>
                        <span class="tenant-name">${tenant.displayName}</span>
                        ${tenant.isAdmin ? '<span class="admin-badge">ADMIN</span>' : ''}
                    </div>
                    <span class="tenant-id">${tenant.tenantId}</span>
                </div>
                <div class="tenant-storage">
                    <div class="storage-item">
                        <div class="storage-label">Used</div>
                        <div class="storage-value">${formatBytes(tenant.currentUsageBytes)}</div>
                    </div>
                    <div class="storage-item">
                        <div class="storage-label">Available</div>
                        <div class="storage-value">${formatBytes(tenant.availableSpaceBytes)}</div>
                    </div>
                    <div class="storage-item">
                        <div class="storage-label">Total</div>
                        <div class="storage-value">${formatBytes(tenant.storageLimitBytes)}</div>
                    </div>
                    <div class="storage-item">
                        <div class="storage-label">Usage</div>
                        <div class="storage-value">${tenant.usagePercentage.toFixed(1)}%</div>
                    </div>
                </div>
                <div class="tenant-actions">
                    <button class="btn btn-secondary" onclick="updateTenantStorage('${tenant.tenantId}', ${tenant.storageLimitBytes})">Update Storage</button>
                    <button class="btn btn-danger" onclick="deleteTenant('${tenant.tenantId}')">Delete</button>
                </div>
            </div>
        `).join('');
        
    } catch (error) {
        console.error('Failed to load tenants:', error);
        document.getElementById('tenants-list').innerHTML = '<div class="loading">Failed to load tenants</div>';
    }
}

async function updateTenantStorage(tenantId, currentLimit) {
    const newLimit = prompt(`Enter new storage limit for tenant ${tenantId} (current: ${formatBytes(currentLimit)}):`);
    if (!newLimit || isNaN(newLimit)) return;
    
    try {
        const response = await makeApiRequestWithBody(`/api/admin/tenants/${tenantId}/storage-limit`, 'PUT', {
            storageLimitBytes: parseInt(newLimit)
        });
        
        await showAlert(`Storage limit updated successfully for ${tenantId}`, 'Update Complete', 'success');
        await loadTenants();
        
    } catch (error) {
        console.error('Failed to update storage limit:', error);
        await showAlert('Failed to update storage limit: ' + error.message, 'Update Failed', 'error');
    }
}

async function deleteTenant(tenantId) {
    const confirmed = await showConfirm(`Are you sure you want to delete tenant '${tenantId}'? This action cannot be undone.`, 'Delete Tenant');
    if (!confirmed) return;
    
    try {
        const response = await fetch(`${API_BASE}/api/admin/tenants/${tenantId}`, {
            method: 'DELETE',
            headers: {
                'X-API-Key': currentApiKey
            }
        });
        
        if (!response.ok) {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Failed to delete tenant');
        }
        
        await showAlert(`Tenant '${tenantId}' deleted successfully`, 'Delete Complete', 'success');
        await loadTenants();
        
    } catch (error) {
        console.error('Failed to delete tenant:', error);
        await showAlert('Failed to delete tenant: ' + error.message, 'Delete Failed', 'error');
    }
}

// Theme management
let currentTheme = 'auto'; // 'auto', 'light', or 'dark'

function toggleTheme() {
    const themeToggle = document.getElementById('theme-toggle');
    const themeIcon = themeToggle.querySelector('.theme-icon');
    
    if (currentTheme === 'auto') {
        // If currently auto, switch to the opposite of system preference
        const systemPrefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        currentTheme = systemPrefersDark ? 'light' : 'dark';
    } else if (currentTheme === 'light') {
        currentTheme = 'dark';
    } else {
        currentTheme = 'light';
    }
    
    applyTheme();
    updateThemeIcon();
    localStorage.setItem('byteshelf-theme', currentTheme);
}

function applyTheme() {
    const root = document.documentElement;
    
    if (currentTheme === 'auto') {
        // Remove any forced theme and let CSS handle it
        root.removeAttribute('data-theme');
    } else {
        // Force the specific theme
        root.setAttribute('data-theme', currentTheme);
    }
}

function updateThemeIcon() {
    const themeToggle = document.getElementById('theme-toggle');
    const themeIcon = themeToggle.querySelector('.theme-icon');
    
    if (currentTheme === 'auto') {
        const systemPrefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        themeIcon.textContent = systemPrefersDark ? '🌙' : '☀️';
    } else if (currentTheme === 'light') {
        themeIcon.textContent = '☀️';
    } else {
        themeIcon.textContent = '🌙';
    }
}

function initializeTheme() {
    // Load saved theme preference
    const savedTheme = localStorage.getItem('byteshelf-theme');
    if (savedTheme) {
        currentTheme = savedTheme;
    }
    
    applyTheme();
    updateThemeIcon();
    
    // Listen for system theme changes when in auto mode
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    mediaQuery.addEventListener('change', () => {
        if (currentTheme === 'auto') {
            updateThemeIcon();
        }
    });
}

// Initialize theme when the page loads
document.addEventListener('DOMContentLoaded', initializeTheme);

// Initialize
document.addEventListener('DOMContentLoaded', function() {
    // Close modal when clicking outside
    window.addEventListener('click', function(event) {
        const modal = document.getElementById('file-modal');
        if (event.target === modal) {
            closeModal();
        }
    });
    
    // Enter key to authenticate
    document.getElementById('api-key').addEventListener('keypress', function(event) {
        if (event.key === 'Enter') {
            authenticate();
        }
    });
    
    // Admin form submission
    document.getElementById('create-tenant-form').addEventListener('submit', async function(event) {
        event.preventDefault();
        
        const tenantId = document.getElementById('new-tenant-id').value.trim();
        const apiKey = document.getElementById('new-api-key').value.trim();
        const displayName = document.getElementById('new-display-name').value.trim();
        const storageLimit = parseInt(document.getElementById('new-storage-limit').value);
        const isAdmin = document.getElementById('new-is-admin').checked;
        
        if (!tenantId || !apiKey || !displayName || !storageLimit) {
            await showAlert('Please fill in all required fields', 'Validation Error', 'error');
            return;
        }
        
        try {
            const response = await makeApiRequestWithBody('/api/admin/tenants', 'POST', {
                tenantId: tenantId,
                apiKey: apiKey,
                displayName: displayName,
                storageLimitBytes: storageLimit,
                isAdmin: isAdmin
            });
            
            await showAlert(`Tenant '${tenantId}' created successfully`, 'Create Complete', 'success');
            
            // Clear form
            document.getElementById('create-tenant-form').reset();
            
            // Switch to tenants tab and refresh
            switchTab('tenants');
            await loadTenants();
            
        } catch (error) {
            console.error('Failed to create tenant:', error);
            await showAlert('Failed to create tenant: ' + error.message, 'Create Failed', 'error');
        }
    });
}); 