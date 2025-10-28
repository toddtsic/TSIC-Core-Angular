// Generate SSL certificates for Angular development using Node.js
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

console.log('Generating SSL certificates for Angular development...\n');

const sslDir = path.join(__dirname, '..', 'TSIC-Core-Angular', 'src', 'frontend', 'tsic-app', 'ssl');

// Ensure SSL directory exists
if (!fs.existsSync(sslDir)) {
    fs.mkdirSync(sslDir, { recursive: true });
}

const certPath = path.join(sslDir, 'localhost.crt');
const keyPath = path.join(sslDir, 'localhost.key');

try {
    // Check if devcert is installed
    try {
        require.resolve('devcert');
    } catch {
        console.log('Installing devcert package...');
        execSync('npm install --save-dev devcert', {
            cwd: path.join(__dirname, '..', 'TSIC-Core-Angular', 'src', 'frontend', 'tsic-app'),
            stdio: 'inherit'
        });
    }

    const devcert = require('devcert');

    console.log('Generating certificate for localhost...');

    devcert.certificateFor('localhost').then(({ key, cert }) => {
        fs.writeFileSync(keyPath, key);
        fs.writeFileSync(certPath, cert);

        console.log('\n✅ SSL certificates created successfully!');
        console.log(`Location: ${sslDir}`);
        console.log('\nFiles created:');
        console.log('  - localhost.crt (certificate)');
        console.log('  - localhost.key (private key)');
        console.log('\nNext steps:');
        console.log('  1. Restart your Angular dev server');
        console.log('  2. Navigate to https://localhost:4200');
        console.log('  3. The certificate is automatically trusted!\n');
    }).catch(err => {
        console.error('Error generating certificate:', err);
        process.exit(1);
    });

} catch (error) {
    console.error('Error:', error.message);
    process.exit(1);
}
