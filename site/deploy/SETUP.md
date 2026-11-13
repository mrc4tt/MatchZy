# Hosting matchzy.miksen.me on the RE server

One-time setup for `site/deploy.sh`. Same pattern as `img.miksen.me` and `bins.miksen.me`:
Cloudflare-proxied, ufw opens 443 to Cloudflare only, the shared `miksen-public` certificate
(Let's Encrypt, `dns-cloudflare`), static files under `/srv`.

Layout on the server:

```
/srv/matchzy-docs/
  releases/20261110-181500-281d519/   one folder per deploy (the last 5 are kept)
  current -> releases/20261110-181500-281d519
```

The upload user `matchzy-deploy` can only run rsync inside `/srv/matchzy-docs` (`rrsync`), so the
deploy key cannot open a shell or touch anything else.

`deploy.sh` connects to `re.miksen.me` (the public address), not the tailnet name `re`: Tailscale
SSH answers port 22 on the tailnet address, authenticates by tailnet policy ("tailnet policy does
not permit you to SSH as user matchzy-deploy") and would skip the `rrsync` restriction anyway.

## 1. Deploy key (on your machine)

```sh
ssh-keygen -t ed25519 -f ~/.ssh/matchzy_deploy -N '' -C matchzy-deploy
cat ~/.ssh/matchzy_deploy.pub
```

## 2. Upload user and folders (on RE, as root)

Put the public key from step 1 in `PUBKEY`:

```sh
PUBKEY='ssh-ed25519 AAAA... matchzy-deploy'
useradd --system --create-home --shell /bin/sh matchzy-deploy
install -d -o matchzy-deploy -g matchzy-deploy -m 755 /srv/matchzy-docs /srv/matchzy-docs/releases
install -d -o matchzy-deploy -g matchzy-deploy -m 700 /home/matchzy-deploy/.ssh
echo "command=\"/usr/bin/rrsync /srv/matchzy-docs\",restrict $PUBKEY" > /home/matchzy-deploy/.ssh/authorized_keys
chown matchzy-deploy:matchzy-deploy /home/matchzy-deploy/.ssh/authorized_keys
chmod 600 /home/matchzy-deploy/.ssh/authorized_keys
# If sshd only lets listed users in, add matchzy-deploy there:
sshd -T | grep -iE '^(allowusers|allowgroups)'
```

## 3. Certificate: add matchzy.miksen.me to miksen-public (on RE, as root)

The domain list is read from the current certificate, so names added since (for example
`simpleadmin.miksen.me`) are kept: `--expand` drops every name that is not passed.

```sh
CRED=$(awk -F' = ' '/^dns_cloudflare_credentials/ {print $2}' /etc/letsencrypt/renewal/miksen-public.conf)
DOMAINS=$(openssl x509 -in /etc/letsencrypt/live/miksen-public/fullchain.pem -noout -ext subjectAltName \
  | grep -o 'DNS:[^,]*' | sed 's/^DNS:/-d /' | tr '\n' ' ')
certbot certonly --dns-cloudflare --dns-cloudflare-credentials "$CRED" \
  --dns-cloudflare-propagation-seconds 30 --key-type ecdsa \
  --cert-name miksen-public --expand \
  $DOMAINS -d matchzy.miksen.me
openssl x509 -in /etc/letsencrypt/live/miksen-public/fullchain.pem -noout -ext subjectAltName
```

## 4. First upload (on your machine)

```sh
cd site && ./deploy.sh
```

The smoke test at the end warns until DNS points at RE (step 6); that is expected now.

## 5. nginx (vhost file from your machine, then on RE as root)

The vhost sets `disable_symlinks if_not_owner from=/srv/matchzy-docs`: nginx only follows a
symlink when it and its target have the same owner, so a leaked deploy key cannot upload a link
to a system file and have it served. `current` and the releases are all owned by `matchzy-deploy`
(step 2), so the normal layout keeps working.

```sh
scp site/deploy/matchzy.miksen.me.conf re:/etc/nginx/sites-available/
```

```sh
ln -s /etc/nginx/sites-available/matchzy.miksen.me.conf /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
# Test the origin before DNS moves: must print the version, then 301 to the slash, then 404.
curl -sk --resolve matchzy.miksen.me:443:127.0.0.1 https://matchzy.miksen.me/ | grep -o 'MatchZy [0-9.]*' | head -1
curl -sk -o /dev/null -w '%{http_code} %{redirect_url}\n' --resolve matchzy.miksen.me:443:127.0.0.1 https://matchzy.miksen.me/guides/coaching
curl -sk -o /dev/null -w '%{http_code}\n' --resolve matchzy.miksen.me:443:127.0.0.1 https://matchzy.miksen.me/nope/
```

## 6. Switch DNS (Cloudflare dashboard)

`matchzy.miksen.me` now points at GitHub Pages. Replace it with the same proxied (orange cloud)
A/AAAA records as `img.miksen.me`. SSL mode as for the other hosts (Full strict). Then:

```sh
curl -sI https://matchzy.miksen.me/ | grep -iE '^(HTTP|server|cache-control|cf-cache-status)'
```

## 7. Retire the old site (after the new one is live)

- GitHub: Settings, Pages: remove the custom domain and turn Pages off.
- Repository: delete `.github/workflows/docs.yml` and `documentation/`, move
  `documentation/docs/whats-new.md` (read by `.github/workflows/release.yml`,
  `site/src/data/changelog.ts` and `.claude/docs/release.md`) and drop
  `site/scripts/convert-mkdocs.mjs`.

## Everyday use

```sh
cd site
./deploy.sh                 # build + publish
./deploy.sh --list          # releases on the server
./deploy.sh --rollback ID   # back to an earlier one
```
