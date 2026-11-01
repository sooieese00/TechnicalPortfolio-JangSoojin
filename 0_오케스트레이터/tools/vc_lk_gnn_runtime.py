# -*- coding: utf-8 -*-

from __future__ import print_function

import base64
import json
import math
import operator
import struct
try:
    from itertools import imap as lk_imap
except ImportError:
    lk_imap = map


class VcLkWeights(object):
    def __init__(self, manifest_json, weights_b64):
        self.manifest = json.loads(manifest_json)
        if self.manifest.get("format") != "dt-keti-lk-gnn-weights/v2":
            raise ValueError("Unsupported LkGnn weight format")
        self.model = self.manifest["model"]
        raw = base64.b64decode(weights_b64)
        values = struct.unpack("<{}f".format(len(raw) // 4), raw)
        self.tensors = {}
        for item in self.manifest["tensors"]:
            offset, count = int(item["floatOffset"]), int(item["floatCount"])
            self.tensors[item["name"]] = (item["shape"], values[offset:offset + count])


class VcLkGnn(object):
    def __init__(self, weights):
        self.w = weights
        self.hidden = int(weights.model["hidden"])
        self.rounds = int(weights.model["rounds"])
        self.neighbor_types = list(weights.model["neighborTypes"])
        self.candidate_dim = int(weights.model["candidateExtraDim"])
        self.global_dim = int(weights.model.get("globalDim", 0))
        self.context = bool(weights.model["context"])
        self.scorer = weights.model["scorer"]
        self.pool = weights.model["pool"]
        self.linear_parameters = {}
        for name in weights.tensors:
            if not name.endswith(".weight"):
                continue
            prefix = name[:-7]
            bias_name = prefix + ".bias"
            if bias_name not in weights.tensors:
                continue
            shape, flat = weights.tensors[name]
            if len(shape) != 2:
                continue
            rows, columns = int(shape[0]), int(shape[1])
            matrix = tuple(flat[row * columns:(row + 1) * columns] for row in range(rows))
            unused, bias = weights.tensors[bias_name]
            self.linear_parameters[prefix] = (rows, columns, matrix, bias)

    @staticmethod
    def dot(row, vector, bias):
        return sum(lk_imap(operator.mul, row, vector), bias)

    def linear(self, prefix, vector):
        rows, columns, matrix, bias = self.linear_parameters[prefix]
        if len(vector) != columns:
            raise ValueError("{} input width {} != {}".format(prefix, len(vector), columns))
        return [self.dot(matrix[row], vector, bias[row]) for row in range(rows)]

    def mlp(self, prefix, vector):
        hidden = self.linear(prefix + ".0", vector)
        hidden = [value if value >= 0.0 else 0.01 * value for value in hidden]
        return self.linear(prefix + ".3", hidden)

    @staticmethod
    def context_features(rows):
        if not rows:
            return []
        count, width = len(rows), len(rows[0])
        columns = [[row[f] for row in rows] for f in range(width)]
        limits = [(min(values), max(values)) for values in columns]
        denom = float(max(count - 1, 1))
        result = []
        for i in range(count):
            rank, reverse, dist_min, dist_max = [], [], [], []
            for f in range(width):
                values = columns[f]
                lo, hi = limits[f]
                r = sum(1 for value in values if value > rows[i][f]) / denom
                span = max(hi - lo, 1.0e-6)
                rank.append(r); reverse.append(1.0 - r)
                dist_min.append((rows[i][f] - lo) / span)
                dist_max.append((hi - rows[i][f]) / span)
            result.append(rank + reverse + dist_min + dist_max)
        return result

    def forward(self, data):
        x = data["x"]
        h = [self.mlp("enc", row) for row in x]
        for layer in range(self.rounds):
            messages = []
            source_vectors = [h[node] + x[node] for node in range(len(x))]
            for neighbor_type in self.neighbor_types:
                aggregate = [[0.0] * self.hidden for unused in x]
                counts = [0] * len(x)
                edge = data["nbr"].get(neighbor_type, [[], []])


                source_messages = {}
                for source, destination in zip(edge[0], edge[1]):
                    message = source_messages.get(source)
                    if message is None:
                        message = self.mlp(
                            "msg.{}.{}".format(layer, neighbor_type),
                            source_vectors[source])
                        source_messages[source] = message
                    for d in range(self.hidden):
                        aggregate[destination][d] += message[d]
                    counts[destination] += 1
                for node in range(len(x)):
                    divisor = float(max(counts[node], 1))
                    aggregate[node] = [value / divisor for value in aggregate[node]]
                messages.append(aggregate)
            updated = []
            for node in range(len(x)):
                vector = []
                for aggregate in messages:
                    vector += aggregate[node]
                updated.append(self.mlp("upd.{}".format(layer), vector + h[node] + x[node]))
            h = updated
        mean = [0.0] * self.hidden
        for row in h:
            for d in range(self.hidden):
                mean[d] += row[d]
        mean = [value / float(max(len(h), 1)) for value in mean]
        if self.pool == "none":
            mean = [0.0] * self.hidden
        global_features = list(data.get("glob", [])[:self.global_dim])
        graph_context = mean + global_features

        extra = [list(row[:self.candidate_dim]) for row in data["candidateExtra"]]
        if self.context:
            context = self.context_features(extra)
            extra = [row + context[i] for i, row in enumerate(extra)]
        z = [h[node] + extra[i] for i, node in enumerate(data["candidateNode"])]
        if self.scorer != "pair":
            return [self.mlp("dec", h[node] + mean + extra[i] + global_features)[0]
                    for i, node in enumerate(data["candidateNode"])]


        count = len(z)
        totals = [0.0] * count
        for i in range(count):
            zi = z[i]
            for j in range(i + 1, count):
                zj = z[j]
                delta = [a - b for a, b in zip(zi, zj)]
                forward = zi + zj + delta + graph_context
                reverse = zj + zi + [-value for value in delta] + graph_context
                margin = self.mlp("pair", forward)[0] - self.mlp("pair", reverse)[0]
                totals[i] += margin
                totals[j] -= margin
        divisor = float(max(count - 1, 1))
        return [value / divisor for value in totals]
