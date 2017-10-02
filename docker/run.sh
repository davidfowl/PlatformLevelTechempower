#!/usr/bin/env bash

#echo on
set -x

docker run \
    -it \
    --rm \
    --name platform-level-techempower \
    --network host \
    platform-level-techempower \
    "$@"
