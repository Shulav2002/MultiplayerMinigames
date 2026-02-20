const crypto = require("crypto");

function id(prefix) {
  return `${prefix}_${crypto.randomBytes(5).toString("hex")}`;
}

module.exports = { id };
