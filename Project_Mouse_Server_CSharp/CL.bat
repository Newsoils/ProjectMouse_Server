cd "C:\Program Files\OpenSSL-Win64\bin"

set OPENSSL_CONF=C:\Program Files\OpenSSL-Win64\openssl.cnf

openssl req -x509 -newkey rsa:4096 -sha256 -nodes -keyout test.key -out test.crt -subj "/CN=test.com" -days 3650

openssl pkcs7 -in test.p7b -inform DER -out result.pem -print_certs

openssl pkcs12 -export -inkey test.key  -in result.pem -name test.com -out final_result.pfx
PW123456789
PW123456789