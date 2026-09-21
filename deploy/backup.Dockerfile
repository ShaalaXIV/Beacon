FROM alpine:3.22.1@sha256:4bcff63911fcb4448bd4fdacec207030997caf25e9bea4045fa6c8c44de311d1

RUN apk add --no-cache sqlite \
    && addgroup -g 1654 compass \
    && adduser -D -H -u 1654 -G compass compass

COPY --chmod=0555 deploy/backup.sh /usr/local/bin/compass-backup
USER 1654:1654
ENTRYPOINT ["/usr/local/bin/compass-backup"]
