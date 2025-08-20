mergeInto(LibraryManager.library, {
    OpenFileDialog: function(gameObjectNamePtr, methodNamePtr, acceptPtr) {
        let gameObjectName = UTF8ToString(gameObjectNamePtr);
        let methodName = UTF8ToString(methodNamePtr);
        let accept = UTF8ToString(acceptPtr);

        let fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.accept = accept || 'image/*';

        fileInput.onchange = function(event) {
            let file = event.target.files[0];
            if (!file) return;
            
            let reader = new FileReader();
            reader.onload = function(e) {
                let img = new Image();
                img.onload = function() {
                    // Resize not to exceed the max callback size
                    let maxSize = 640;
                    let width = img.width;
                    let height = img.height;
                    let scale = 1;
                    
                    // Calculate size
                    if (width > height) {
                        if (width > maxSize) {
                            scale = maxSize / width;
                        }
                    } else {
                        if (height > maxSize) {
                            scale = maxSize / height;
                        }
                    }
                    
                    let newWidth = Math.floor(width * scale);
                    let newHeight = Math.floor(height * scale);
                    
                    // Resize on canvas
                    let canvas = document.createElement('canvas');
                    canvas.width = newWidth;
                    canvas.height = newHeight;
                    let ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0, newWidth, newHeight);
                    
                    // Convert to ArrayBuffer
                    canvas.toBlob(function(blob) {
                        let blobReader = new FileReader();
                        blobReader.onload = function(e) {
                            let arrayBuffer = e.target.result;
                            let bytes = new Uint8Array(arrayBuffer);
                            
                            let base64 = btoa(String.fromCharCode.apply(null, bytes));
                            
                            SendMessage(gameObjectName, methodName, base64);
                        };
                        blobReader.readAsArrayBuffer(blob);
                    }, 'image/jpeg', 0.9);
                };
                img.src = URL.createObjectURL(file);
            };
            reader.readAsArrayBuffer(file);
        };
        
        fileInput.click();
    }
});
