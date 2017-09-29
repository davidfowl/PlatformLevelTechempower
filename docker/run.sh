#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --name platform-level-techempower \
    --network host \
    platform-level-techempower \
    "$@"
